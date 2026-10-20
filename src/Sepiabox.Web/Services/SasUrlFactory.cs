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
    /// How long a browser may cache a redirect to a derivative: a minute less than the shortest link's
    /// life, so a cached redirect never points at a link that has just expired (clock skew, slow loads).
    /// </summary>
    public static readonly TimeSpan DerivativeCacheDuration = DerivativeLifetime - DerivativeWindow - TimeSpan.FromMinutes(1);

    /// <summary>
    /// Video links expire at the end of the 5-minute window they were signed in plus this (3 h 55 to 4 h
    /// left), so a long camcorder tape, or one paused for a while, still plays to the end (D-049).
    /// </summary>
    public static readonly TimeSpan VideoLifetime = TimeSpan.FromHours(4);

    public static readonly TimeSpan OriginalLifetime = TimeSpan.FromMinutes(5);

    // Longer than the longest link, so a key can be reused for a while before it is too close to expiry.
    private static readonly TimeSpan s_delegationKeyLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan s_clockSkew = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private volatile UserDelegationKey? _delegationKey;

    public Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default) =>
        CreateAsync(StorageContainers.Derivatives, blobPath, WindowStart() + DerivativeLifetime, configure: null, cancellationToken);

    public Task<Uri> ForVideoAsync(string blobPath, CancellationToken cancellationToken = default) =>
        CreateAsync(StorageContainers.Derivatives, blobPath, WindowStart() + VideoLifetime, configure: null, cancellationToken);

    public Task<Uri> ForOriginalAsync(string assetId, OriginalFile file, CancellationToken cancellationToken = default) =>
        CreateAsync(StorageContainers.Originals, assetId, time.GetUtcNow() + OriginalLifetime, sas =>
        {
            // Originals are stored under their hash, so the name and type come from the catalog.
            sas.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(file.FileName)}";
            sas.ContentType = file.ContentType;
        }, cancellationToken);

    public void Dispose() => _keyLock.Dispose();

    // Links signed in the same window are identical, so the browser cache can reuse them.
    private DateTimeOffset WindowStart()
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

    // A key must outlive every link signed with it: a user delegation SAS stops working when its key expires.
    private bool IsStillGood(UserDelegationKey key) =>
        key.SignedExpiresOn - time.GetUtcNow() >= VideoLifetime + s_clockSkew;
}
