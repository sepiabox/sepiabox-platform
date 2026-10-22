using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Sepiabox.Storage.Azure;

/// <summary>
/// <see cref="IBlobStore"/> on Azure Blob Storage (S-050, D-051). Versions are ETags, kept without their quotes, and a
/// conditional write is <c>If-Match</c> or <c>If-None-Match: *</c>, with 409 and 412 as a lost condition. Read URLs are
/// signed with the account key when the client has one (Azurite) and with a user delegation key otherwise (Azure,
/// managed identity), which never exposes an account key (D-039); the key is requested once and reused while it outlives
/// the longest link (D-049).
/// </summary>
public sealed class AzureBlobStore(BlobServiceClient service, TimeProvider? time = null) : IBlobStore, IDisposable
{
    /// <summary>
    /// The longest a link this store signs may last (a video's, D-049). A delegation key is reused while it outlives
    /// this by the clock skew, so one key serves about two hours of signing.
    /// </summary>
    public static readonly TimeSpan MaxReadUrlLifetime = TimeSpan.FromHours(4);

    private static readonly TimeSpan s_clockSkew = TimeSpan.FromMinutes(5);

    // A user delegation SAS stops working when its key expires.
    private static readonly TimeSpan s_delegationKeyLifetime = MaxReadUrlLifetime + TimeSpan.FromHours(2);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private volatile UserDelegationKey? _delegationKey;

    public async Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default) =>
        (await service.GetBlobContainerClient(container).ExistsAsync(cancellationToken)).Value;

    public async Task<BlobContent?> ReadAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = (await Blob(container, name).DownloadContentAsync(cancellationToken)).Value;
            return new BlobContent(current.Content, Unquoted(current.Details.ETag), current.Details.LastModified);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async IAsyncEnumerable<BlobListing> ListAsync(
        string container, string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var blob in service.GetBlobContainerClient(container).GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
        {
            yield return Listing(blob);
        }
    }

    public async Task<BlobPage> ListPageAsync(
        string container, string? prefix, string? continuationToken, int pageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var pages = service.GetBlobContainerClient(container)
                .GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken)
                .AsPages(continuationToken, pageSize)
                .GetAsyncEnumerator(cancellationToken);
            if (!await pages.MoveNextAsync())
            {
                return new BlobPage([], null);
            }

            var page = pages.Current;
            return new BlobPage([.. page.Values.Select(Listing)], string.IsNullOrEmpty(page.ContinuationToken) ? null : page.ContinuationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No container yet: nothing to list.
            return new BlobPage([], null);
        }
    }

    public async Task<BlobWriteResult> WriteAsync(
        string container, string name, BinaryData content, string contentType, BlobWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = condition.Kind switch
            {
                BlobWriteConditionKind.IfNew => new BlobRequestConditions { IfNoneMatch = ETag.All },
                BlobWriteConditionKind.IfVersion => new BlobRequestConditions { IfMatch = new ETag(Quoted(condition.Version!)) },
                _ => null,
            },
        };
        try
        {
            var written = (await Blob(container, name).UploadAsync(content, options, cancellationToken)).Value;
            return new BlobWriteResult(BlobWriteStatus.Written, Unquoted(written.ETag));
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412 || (ex.Status == 404 && condition.Kind == BlobWriteConditionKind.IfVersion))
        {
            // Someone wrote the blob first (409 for a blob that exists, 412 for a version that changed), or deleted it.
            return new BlobWriteResult(BlobWriteStatus.ConditionNotMet);
        }
    }

    public async Task<bool> DeleteAsync(string container, string name, CancellationToken cancellationToken = default) =>
        (await Blob(container, name).DeleteIfExistsAsync(cancellationToken: cancellationToken)).Value;

    public async Task<Uri> CreateReadUrlAsync(
        string container, string name, DateTimeOffset expiresOn, BlobReadUrlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (expiresOn > _time.GetUtcNow() + MaxReadUrlLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresOn), expiresOn, $"A link lasts at most {MaxReadUrlLifetime}.");
        }

        var blob = Blob(container, name);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expiresOn)
        {
            BlobContainerName = container,
            BlobName = name,
            Resource = "b",
            Protocol = blob.Uri.Scheme == Uri.UriSchemeHttps ? SasProtocol.Https : SasProtocol.HttpsAndHttp,
        };
        if (options?.DownloadFileName is { } fileName)
        {
            sas.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
        }

        if (options?.ContentType is { } contentType)
        {
            sas.ContentType = contentType;
        }

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas);
        }

        var key = await GetDelegationKeyAsync(cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key, service.AccountName) }.ToUri();
    }

    public void Dispose() => _keyLock.Dispose();

    private static BlobListing Listing(BlobItem blob) =>
        new(blob.Name, Unquoted(blob.Properties.ETag), blob.Properties.LastModified ?? DateTimeOffset.MinValue);

    private BlobClient Blob(string container, string name) => service.GetBlobContainerClient(container).GetBlobClient(name);

    // Listings give an ETag bare and downloads quoted, as in the HTTP header: versions are kept bare, and quoted to send.
    private static string Unquoted(ETag? etag) => etag?.ToString().Trim('"') ?? string.Empty;

    private static string Quoted(string version) => version.StartsWith('"') ? version : $"\"{version}\"";

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
                var now = _time.GetUtcNow();
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
        key.SignedExpiresOn - _time.GetUtcNow() >= MaxReadUrlLifetime + s_clockSkew;
}
