using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Sepiabox.Web.Services;

/// <summary>Short-lived read-only links to blobs, so the browser never gets storage credentials (D-004).</summary>
public interface ISasUrlFactory
{
    /// <summary>A thumbnail, preview or poster in the <c>derivatives</c> container.</summary>
    Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>The original file in the <c>originals</c> container.</summary>
    Task<Uri> ForOriginalAsync(string assetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Signs with the account key when the client has one (Azurite) and with a user delegation key otherwise
/// (Azure, managed identity), which never exposes an account key to the web.
/// </summary>
public sealed class SasUrlFactory(BlobServiceClient service, TimeProvider time) : ISasUrlFactory, IDisposable
{
    public static readonly TimeSpan DerivativeLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan OriginalLifetime = TimeSpan.FromMinutes(5);

    // A delegation key is requested from Azure, so it is reused while it outlives any link signed with it.
    private static readonly TimeSpan s_delegationKeyLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan s_clockSkew = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private UserDelegationKey? _delegationKey;

    public Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default) =>
        CreateAsync(StorageContainers.Derivatives, blobPath, DerivativeLifetime, cancellationToken);

    public Task<Uri> ForOriginalAsync(string assetId, CancellationToken cancellationToken = default) =>
        CreateAsync(StorageContainers.Originals, assetId, OriginalLifetime, cancellationToken);

    private async Task<Uri> CreateAsync(string container, string blobName, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var blob = service.GetBlobContainerClient(container).GetBlobClient(blobName);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, time.GetUtcNow() + lifetime)
        {
            BlobContainerName = container,
            BlobName = blobName,
            Resource = "b",
        };

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas);
        }

        var key = await GetDelegationKeyAsync(cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key, service.AccountName) }.ToUri();
    }

    private async Task<UserDelegationKey> GetDelegationKeyAsync(CancellationToken cancellationToken)
    {
        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow();
            // Renewed while it still covers the longest link, so no link outlives its key.
            if (_delegationKey is null || _delegationKey.SignedExpiresOn - now < DerivativeLifetime + s_clockSkew)
            {
                _delegationKey = await service.GetUserDelegationKeyAsync(
                    now - s_clockSkew, now + s_delegationKeyLifetime, cancellationToken);
            }

            return _delegationKey;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    public void Dispose() => _keyLock.Dispose();
}
