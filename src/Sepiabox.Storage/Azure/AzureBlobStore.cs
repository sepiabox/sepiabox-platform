using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;

namespace Sepiabox.Storage.Azure;

/// <summary>
/// <see cref="IBlobStore"/> on Azure Blob Storage (S-050, D-051). Versions are ETags, kept without their quotes, and a
/// conditional write is <c>If-Match</c> or <c>If-None-Match: *</c>, with 409 and 412 as a lost condition. Read URLs are
/// signed with the account key when the client has one (Azurite) and with a user delegation key otherwise (Azure,
/// managed identity), which never exposes an account key (D-039); the key is requested once and reused while it outlives
/// the longest link (D-049).
/// </summary>
/// <param name="service">The account's client.</param>
/// <param name="time">The clock.</param>
/// <param name="maxReadUrlLifetime">
/// The longest a link may last; <see cref="MaxReadUrlLifetime"/> (4 hours) unless given. The export's originals list asks
/// for 24 hours (S-040, D-110): its store is made with that, and the delegation key then lasts that long and two hours more.
/// </param>
public sealed class AzureBlobStore(BlobServiceClient service, TimeProvider? time = null, TimeSpan? maxReadUrlLifetime = null) : IBlobStore, IDisposable
{
    /// <summary>
    /// The longest a link this store signs may last (a video's, D-049). A delegation key is reused while it outlives
    /// this by the clock skew, so one key serves about two hours of signing.
    /// </summary>
    public static readonly TimeSpan MaxReadUrlLifetime = TimeSpan.FromHours(4);

    private static readonly TimeSpan s_clockSkew = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _maxLinkLifetime = maxReadUrlLifetime ?? MaxReadUrlLifetime;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private volatile UserDelegationKey? _delegationKey;

    public async Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default) =>
        (await service.GetBlobContainerClient(container).ExistsAsync(cancellationToken)).Value;

    // A request storage refused or did not answer, or an identity that could not get a token to make it (signing a link
    // asks storage for a delegation key with the managed identity).
    public bool IsStoreFailure(Exception exception) => exception is RequestFailedException or global::Azure.Identity.AuthenticationFailedException;

    public Task CreateContainerAsync(string container, CancellationToken cancellationToken = default) =>
        service.GetBlobContainerClient(container).CreateIfNotExistsAsync(cancellationToken: cancellationToken);

    public async Task<long?> GetSizeAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        try
        {
            return (await Blob(container, name).GetPropertiesAsync(cancellationToken: cancellationToken)).Value.ContentLength;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<DateTimeOffset?> GetServerTimeAsync(string container, CancellationToken cancellationToken = default) =>
        (await service.GetBlobContainerClient(container).GetPropertiesAsync(cancellationToken: cancellationToken)).GetRawResponse().Headers.Date;

    public IStagedUpload BeginUpload(string container, string name) => new StagedUpload(service.GetBlobContainerClient(container).GetBlockBlobClient(name));

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
        // A container that is not there has no blobs; the error shows when the first page is asked for.
        await using var blobs = service.GetBlobContainerClient(container)
            .GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            try
            {
                if (!await blobs.MoveNextAsync())
                {
                    yield break;
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                yield break;
            }

            yield return Listing(blobs.Current);
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
        catch (RequestFailedException ex) when (condition.Kind != BlobWriteConditionKind.Always && LostTheCondition(ex, condition))
        {
            // Someone wrote the blob first (409 for a blob that exists, 412 for a version that changed), or deleted it.
            // An unconditional write has no condition to lose: a 409 or 412 there is a real failure.
            return new BlobWriteResult(BlobWriteStatus.ConditionNotMet);
        }
    }

    public async Task<bool> DeleteAsync(string container, string name, CancellationToken cancellationToken = default) =>
        (await Blob(container, name).DeleteIfExistsAsync(cancellationToken: cancellationToken)).Value;

    public async Task<Uri> CreateReadUrlAsync(
        string container, string name, DateTimeOffset expiresOn, BlobReadUrlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (expiresOn > _time.GetUtcNow() + _maxLinkLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresOn), expiresOn, $"A link lasts at most {_maxLinkLifetime}.");
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

    // Azure keeps staged blocks for a week and then drops them, so an upload that is not committed needs no clean-up.
    private sealed class StagedUpload(BlockBlobClient blob) : IStagedUpload
    {
        private readonly List<string> _blockIds = [];
        private bool _committed;

        public async Task AppendAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken = default)
        {
            if (_committed)
            {
                throw new InvalidOperationException("The upload is already committed.");
            }

            // Block ids must all have the same length within a blob.
            var id = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(_blockIds.Count.ToString("D8", System.Globalization.CultureInfo.InvariantCulture)));
            // The caller's buffer is sent as it is when it is an array, so a block is not copied.
            using var content = System.Runtime.InteropServices.MemoryMarshal.TryGetArray(block, out var segment)
                ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
                : new MemoryStream(block.ToArray(), writable: false);
            await blob.StageBlockAsync(id, content, cancellationToken: cancellationToken);
            _blockIds.Add(id);
        }

        public async Task CommitAsync(string contentType, BlobStorageClass storageClass = BlobStorageClass.Default, CancellationToken cancellationToken = default)
        {
            if (_committed)
            {
                throw new InvalidOperationException("The upload is already committed.");
            }

            await blob.CommitBlockListAsync(
                _blockIds,
                new CommitBlockListOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                    AccessTier = storageClass == BlobStorageClass.Cold ? AccessTier.Cold : (AccessTier?)null,
                },
                cancellationToken);
            _committed = true;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    // A missing container is 404 too, and is not a lost condition: only a missing blob is, and only for IfVersion.
    private static bool LostTheCondition(RequestFailedException ex, BlobWriteCondition condition) =>
        ex.Status switch
        {
            412 => true,
            409 => ex.ErrorCode is null or "BlobAlreadyExists" or "ConditionNotMet",
            404 => condition.Kind == BlobWriteConditionKind.IfVersion && ex.ErrorCode == "BlobNotFound",
            _ => false,
        };

    private static BlobListing Listing(BlobItem blob) =>
        new(blob.Name, Unquoted(blob.Properties.ETag), blob.Properties.LastModified ?? DateTimeOffset.MaxValue);

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
                // A user delegation SAS stops working when its key expires.
                key = await service.GetUserDelegationKeyAsync(now - s_clockSkew, now + _maxLinkLifetime + TimeSpan.FromHours(2), cancellationToken);
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
        key.SignedExpiresOn - _time.GetUtcNow() >= _maxLinkLifetime + s_clockSkew;
}
