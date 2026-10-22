using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Sepiabox.Storage.Azure;

namespace Sepiabox.Storage.Tests;

/// <summary>
/// What <see cref="AzureBlobStore.IsStoreFailure"/> calls the store failing a request (D-100): a callers' bug is not one,
/// and neither is a missing local file.
/// </summary>
public sealed class AzureStoreFailureTests : IDisposable
{
    private readonly AzureBlobStore _store = new(new BlobServiceClient("UseDevelopmentStorage=true"));

    public void Dispose() => _store.Dispose();

    public static TheoryData<Exception, bool> Failures => new()
    {
        { new RequestFailedException(503, "ServerBusy"), true },
        { new RequestFailedException(403, "AuthorizationPermissionMismatch"), true },
        { new AuthenticationFailedException("no token"), true },
        { new CredentialUnavailableException("no managed identity"), true },
        { new InvalidOperationException("a bug"), false },
        { new FileNotFoundException("no such file"), false },
        { new IOException("disk"), false },
        { new ArgumentNullException("x"), false },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void Tells_a_failed_request_from_anything_else(Exception exception, bool failure)
    {
        Assert.Equal(failure, _store.IsStoreFailure(exception));
    }

    [Fact]
    public void The_in_memory_store_has_no_failures_of_its_own()
    {
        // It throws InvalidOperationException for a missing container where Azure answers 404 (a RequestFailedException).
        Assert.False(((IBlobStore)new InMemoryBlobStore()).IsStoreFailure(new RequestFailedException(503, "ServerBusy")));
    }
}
