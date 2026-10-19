using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Testcontainers.Azurite;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Pipeline.Tests;

[Trait("Category", "Integration")]
public sealed class ManifestStoreTests : IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        .WithInMemoryPersistence()
        // The storage SDK speaks a newer API version than Azurite announces; Aspire's emulator does the same.
        .WithCommand("--skipApiVersionCheck")
        .Build();

    private readonly ManifestStore _store = new(new ManifestValidator());
    private BlobContainerClient _container = null!;

    public async Task InitializeAsync()
    {
        await _azurite.StartAsync();
        _container = new BlobServiceClient(_azurite.GetConnectionString()).GetBlobContainerClient("manifests");
        await _container.CreateAsync();
    }

    public async Task DisposeAsync() => await _azurite.DisposeAsync();

    [Fact]
    public async Task Writes_a_new_manifest_in_its_stored_form()
    {
        var manifest = Photo(Source("HD da Ana", "2019\\praia.jpg"));

        var result = await _store.WriteMergedAsync(Blob(), manifest, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(ManifestJson.Serialize(ManifestMerge.Canonical(manifest)), await StoredJsonAsync());
    }

    [Fact]
    public async Task Merges_into_the_stored_manifest()
    {
        await _store.WriteMergedAsync(Blob(), Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        var result = await _store.WriteMergedAsync(Blob(), Photo(Source("HD do Jonathas", "b.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(["HD da Ana", "HD do Jonathas"], (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Does_not_write_when_nothing_would_change()
    {
        var manifest = Photo(Source("HD da Ana", "a.jpg"));
        await _store.WriteMergedAsync(Blob(), manifest, CancellationToken.None);
        var etag = (await Blob().GetPropertiesAsync()).Value.ETag;

        var result = await _store.WriteMergedAsync(Blob(), manifest, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Unchanged, result.Status);
        Assert.Equal(etag, (await Blob().GetPropertiesAsync()).Value.ETag);
    }

    [Fact]
    public async Task A_stored_manifest_that_cannot_be_read_is_a_problem_and_is_left_alone()
    {
        await Blob().UploadAsync(BinaryData.FromString("{ \"id\": 42 }"));

        var result = await _store.WriteMergedAsync(Blob(), Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("the manifest in storage cannot be read", result.Problem, StringComparison.Ordinal);
        Assert.Equal("{ \"id\": 42 }", await StoredJsonAsync());
    }

    [Fact]
    public async Task A_stored_manifest_of_another_asset_is_a_problem()
    {
        var other = Photo(Source("HD da Ana", "a.jpg")) with { Id = new string('b', 64) };
        await Blob().UploadAsync(BinaryData.FromString(ManifestJson.Serialize(other)));

        var result = await _store.WriteMergedAsync(Blob(), Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.Contains(other.Id, result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unusable_work_folder_manifest_is_a_problem_and_creates_nothing()
    {
        var incoming = Photo(Source("HD da Ana", "a.jpg")) with { Sources = null! };

        var result = await _store.WriteMergedAsync(Blob(), incoming, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("this manifest is not valid", result.Problem, StringComparison.Ordinal);
        Assert.False(await Blob().ExistsAsync());
    }

    [Fact]
    public async Task A_merge_that_would_store_an_invalid_manifest_is_a_problem_and_writes_nothing()
    {
        await _store.WriteMergedAsync(Blob(), Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);
        var before = await StoredJsonAsync();

        // Width 0 deserializes fine but the schema requires at least 1.
        var result = await _store.WriteMergedAsync(Blob(), Photo(Source("HD do Jonathas", "b.jpg")) with { Width = 0 }, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.Equal(before, await StoredJsonAsync());
    }

    [Fact]
    public async Task Merges_again_when_another_writer_creates_the_blob_first()
    {
        // Our read finds nothing; another upload then creates the blob, so If-None-Match: * fails (409).
        var blob = new InterferingBlob(this, interferences: 1);

        var result = await _store.WriteMergedAsync(blob, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(["Concorrente 1", "HD da Ana"], (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Merges_again_when_another_writer_changes_the_blob_after_our_read()
    {
        // The blob changes between our read and our write, so If-Match fails (412).
        await _store.WriteMergedAsync(Blob(), Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);
        var blob = new InterferingBlob(this, interferences: 1);

        var result = await _store.WriteMergedAsync(blob, Photo(Source("HD do Jonathas", "b.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(["Concorrente 1", "HD da Ana", "HD do Jonathas"], (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Gives_up_with_a_problem_when_every_attempt_conflicts()
    {
        var blob = new InterferingBlob(this, interferences: int.MaxValue);

        var result = await _store.WriteMergedAsync(blob, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.Equal($"gave up after {ManifestStore.MaxAttempts} concurrent changes to the manifest in storage", result.Problem);
        Assert.DoesNotContain("HD da Ana", (await StoredAsync()).Sources.Select(s => s.Name));
    }

    private BlobClient Blob() => _container.GetBlobClient(BlobLayout.ManifestBlobName(PhotoId));

    private async Task<string> StoredJsonAsync() => (await Blob().DownloadContentAsync()).Value.Content.ToString();

    private async Task<Manifest> StoredAsync() => ManifestJson.Deserialize(await StoredJsonAsync());

    /// <summary>
    /// Another upload of the same asset that writes the manifest right after each of our reads, for the
    /// first <c>interferences</c> reads.
    /// </summary>
    private sealed class InterferingBlob(ManifestStoreTests test, int interferences)
        : BlobClient(test._azurite.GetConnectionString(), test._container.Name, BlobLayout.ManifestBlobName(PhotoId))
    {
        private int _written;

        public override async Task<Response<BlobDownloadResult>> DownloadContentAsync(CancellationToken cancellationToken = default)
        {
            Response<BlobDownloadResult>? read = null;
            RequestFailedException? missing = null;
            try
            {
                read = await base.DownloadContentAsync(cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                missing = ex;
            }

            if (_written < interferences)
            {
                _written++;
                var current = missing is null ? ManifestJson.Deserialize(read!.Value.Content.ToString()) : Photo(Source("x", "x"));
                var competitor = Photo(Source($"Concorrente {_written}", "c.jpg"));
                var stored = missing is null ? ManifestMerge.Merge(current, competitor) : competitor;
                await test.Blob().UploadAsync(BinaryData.FromString(ManifestJson.Serialize(stored)), overwrite: true, cancellationToken);
            }

            return missing is null ? read! : throw missing;
        }
    }
}
