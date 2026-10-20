using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Sepiabox.Web.Services;

/// <summary>
/// Short-lived read-only links to blobs, so the browser never gets storage credentials (D-004). The web
/// signs them when the browser asks (see <c>MediaEndpoints</c>), never into a page that may sit open.
/// </summary>
public interface ISasUrlFactory
{
    /// <summary>A thumbnail, preview or poster in the <c>derivatives</c> container.</summary>
    Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// A video's 720p version in the <c>derivatives</c> container, with a link that outlasts playback: the
    /// browser keeps fetching ranges of it for as long as the video plays or sits paused.
    /// </summary>
    Task<Uri> ForVideoAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>The original in the <c>originals</c> container, downloaded under its source file name.</summary>
    Task<Uri> ForOriginalAsync(string assetId, OriginalFile file, CancellationToken cancellationToken = default);
}

/// <summary>
/// Signs with the account key when the client has one (Azurite) and with a user delegation key otherwise
/// (Azure, managed identity), which never exposes an account key to the web (D-039).
/// </summary>
public sealed class SasUrlFactory(BlobServiceClient service, TimeProvider time) : ISasUrlFactory, IDisposable
{
    /// <summary>Derivative links expire at the end of the 5-minute window they were signed in plus this.</summary>
    public static readonly TimeSpan DerivativeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Derivative expiry is rounded down to this window, so every request in it gets the same URL and the
    /// browser cache can reuse the image. A link is therefore valid for 10 to 15 minutes.
    /// </summary>
    public static readonly TimeSpan DerivativeWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a browser may cache a redirect to a derivative or video: a minute less than the shortest
    /// link's life, so a cached redirect never points at a link that has just expired (clock skew, slow loads).
    /// </summary>
    public static readonly TimeSpan RedirectCacheDuration = DerivativeLifetime - DerivativeWindow - TimeSpan.FromMinutes(1);

    /// <summary>
    /// Video links expire at the start of the 5-minute window they were signed in plus this, so 3 h 55 to
    /// 4 h after signing: a long camcorder tape, or one paused for a while, still plays to the end (D-049).
    /// </summary>
    public static readonly TimeSpan VideoLifetime = TimeSpan.FromHours(4);

    public static readonly TimeSpan OriginalLifetime = TimeSpan.FromMinutes(5);

    // Declared after the lifetimes it reads: static fields are initialised in order.
    private static readonly TimeSpan s_longestLink = new[] { DerivativeLifetime, VideoLifetime, OriginalLifetime }.Max();
    private static readonly TimeSpan s_clockSkew = TimeSpan.FromMinutes(5);

    // A user delegation SAS stops working when its key expires, so a key is reused only while it outlives the
    // longest link by the clock skew; the margin lets one key serve about two hours of signing.
    private static readonly TimeSpan s_delegationKeyLifetime = s_longestLink + TimeSpan.FromHours(2);

    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private volatile UserDelegationKey? _delegationKey;

    public Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default) =>
        CreateAsync(StorageContainers.Derivatives, blobPath, SigningWindowStart() + DerivativeLifetime, configure: null, cancellationToken);

    public Task<Uri> ForVideoAsync(string blobPath, CancellationToken cancellationToken = default) =>
        CreateAsync(StorageContainers.Derivatives, blobPath, SigningWindowStart() + VideoLifetime, configure: null, cancellationToken);

    public Task<Uri> ForOriginalAsync(string assetId, OriginalFile file, CancellationToken cancellationToken = default) =>
        CreateAsync(StorageContainers.Originals, assetId, time.GetUtcNow() + OriginalLifetime, sas =>
        {
            // Originals are stored under their hash, so the name and type come from the catalog.
            sas.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(file.FileName)}";
            sas.ContentType = file.ContentType;
        }, cancellationToken);

    public void Dispose() => _keyLock.Dispose();

    // Links signed in the same window are identical, so the browser cache can reuse them.
    private DateTimeOffset SigningWindowStart()
    {
        var now = time.GetUtcNow();
        return new DateTimeOffset(now.UtcTicks - (now.UtcTicks % DerivativeWindow.Ticks), TimeSpan.Zero);
    }

    private async Task<Uri> CreateAsync(
        string container, string blobName, DateTimeOffset expiresOn, Action<BlobSasBuilder>? configure, CancellationToken cancellationToken)
    {
        var blob = service.GetBlobContainerClient(container).GetBlobClient(blobName);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expiresOn)
        {
            BlobContainerName = container,
            BlobName = blobName,
            Resource = "b",
            Protocol = blob.Uri.Scheme == Uri.UriSchemeHttps ? SasProtocol.Https : SasProtocol.HttpsAndHttp,
        };
        configure?.Invoke(sas);

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas);
        }

        var key = await GetDelegationKeyAsync(cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key, service.AccountName) }.ToUri();
    }

    private async Task<UserDelegationKey> GetDelegationKeyAsync(CancellationToken cancellationToken)
    {
        // Most requests find a key that is still good and never touch the lock.
        if (_delegationKey is { } current && IsStillGood(current))
        {
            return current;
        }

        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            if (_delegationKey is not { } key || !IsStillGood(key))
            {
                var now = time.GetUtcNow();
                key = await service.GetUserDelegationKeyAsync(now - s_clockSkew, now + s_delegationKeyLifetime, cancellationToken);
                _delegationKey = key;
            }

            return key;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    private bool IsStillGood(UserDelegationKey key) =>
        key.SignedExpiresOn - time.GetUtcNow() >= s_longestLink + s_clockSkew;
}
