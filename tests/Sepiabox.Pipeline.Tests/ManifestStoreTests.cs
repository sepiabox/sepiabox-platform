using System.Diagnostics.CodeAnalysis;
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

        var result = await _store.WriteMergedAsync(_container, manifest, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(ManifestJson.Serialize(ManifestMerge.Canonical(manifest)), await StoredJsonAsync());
    }

    [Fact]
    public async Task Names_the_blob_after_the_manifest_id()
    {
        var other = Photo(Source("HD da Ana", "a.jpg")) with { Id = new string('c', 64) };

        await _store.WriteMergedAsync(_container, other, CancellationToken.None);

        Assert.True(await _container.GetBlobClient(BlobLayout.ManifestBlobName(other.Id)).ExistsAsync());
        Assert.False(await Blob().ExistsAsync());
    }

    [Fact]
    public async Task Merges_into_the_stored_manifest()
    {
        await _store.WriteMergedAsync(_container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        var result = await _store.WriteMergedAsync(_container, Photo(Source("HD do Jonathas", "b.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(["HD da Ana", "HD do Jonathas"], (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Does_not_write_when_nothing_would_change()
    {
        var manifest = Photo(Source("HD da Ana", "a.jpg"));
        await _store.WriteMergedAsync(_container, manifest, CancellationToken.None);
        var etag = (await Blob().GetPropertiesAsync()).Value.ETag;

        var result = await _store.WriteMergedAsync(_container, manifest, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Unchanged, result.Status);
        Assert.Equal(etag, (await Blob().GetPropertiesAsync()).Value.ETag);
    }

    [Fact]
    public async Task Does_not_write_again_when_the_same_faces_are_uploaded()
    {
        var manifest = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9)] };
        await _store.WriteMergedAsync(_container, manifest, CancellationToken.None);
        var etag = (await Blob().GetPropertiesAsync()).Value.ETag;

        var reread = ManifestJson.Deserialize(ManifestJson.Serialize(manifest));
        var result = await _store.WriteMergedAsync(_container, reread, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Unchanged, result.Status);
        Assert.Equal(etag, (await Blob().GetPropertiesAsync()).Value.ETag);
    }

    [Fact]
    public async Task Uploads_of_different_faces_from_two_computers_settle_on_one_blob()
    {
        var ana = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.91)] };
        var jonathas = Photo(Source("HD do Jonathas", "b.jpg")) with { FacesProcessed = true, Faces = [Face(0.93, axis: 1)] };
        await _store.WriteMergedAsync(_container, ana, CancellationToken.None);
        await _store.WriteMergedAsync(_container, jonathas, CancellationToken.None);
        var etag = (await Blob().GetPropertiesAsync()).Value.ETag;

        Assert.Equal(ManifestWriteStatus.Unchanged, (await _store.WriteMergedAsync(_container, ana, CancellationToken.None)).Status);
        Assert.Equal(ManifestWriteStatus.Unchanged, (await _store.WriteMergedAsync(_container, jonathas, CancellationToken.None)).Status);
        Assert.Equal(etag, (await Blob().GetPropertiesAsync()).Value.ETag);
    }

    [Fact]
    public async Task A_stored_manifest_that_cannot_be_read_is_a_problem_and_is_left_alone()
    {
        await Blob().UploadAsync(BinaryData.FromString("{ \"id\": 42 }"));

        var result = await _store.WriteMergedAsync(_container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("the manifest in storage cannot be read", result.Problem, StringComparison.Ordinal);
        Assert.Equal("{ \"id\": 42 }", await StoredJsonAsync());
    }

    [Fact]
    public async Task A_stored_manifest_of_another_asset_is_a_problem()
    {
        var other = Photo(Source("HD da Ana", "a.jpg")) with { Id = new string('b', 64) };
        await Blob().UploadAsync(BinaryData.FromString(ManifestJson.Serialize(other)));

        var result = await _store.WriteMergedAsync(_container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.Contains(other.Id, result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unusable_work_folder_manifest_is_a_problem_and_creates_nothing()
    {
        var incoming = Photo(Source("HD da Ana", "a.jpg")) with { Sources = null! };

        var result = await _store.WriteMergedAsync(_container, incoming, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("this manifest is not valid", result.Problem, StringComparison.Ordinal);
        Assert.False(await Blob().ExistsAsync());
    }

    [Fact]
    public async Task An_invalid_incoming_manifest_is_a_problem_and_leaves_the_stored_one_alone()
    {
        await _store.WriteMergedAsync(_container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);
        var before = await StoredJsonAsync();

        // Width 0 deserializes fine but the schema requires at least 1.
        var result = await _store.WriteMergedAsync(_container, Photo(Source("HD do Jonathas", "b.jpg")) with { Width = 0 }, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("this manifest is not valid", result.Problem, StringComparison.Ordinal);
        Assert.Equal(before, await StoredJsonAsync());
    }

    [Fact]
    public async Task A_merge_that_would_store_an_invalid_manifest_is_a_problem_and_writes_nothing()
    {
        // Two valid manifests cannot merge into an invalid one under today's schema, so a validator
        // that refuses any manifest with more than one source stands in for a future rule that could.
        var store = new ManifestStore(new RejectsSeveralSources());
        await store.WriteMergedAsync(_container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);
        var before = await StoredJsonAsync();

        var result = await store.WriteMergedAsync(_container, Photo(Source("HD do Jonathas", "b.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("the merged manifest is not valid: more than one source", result.Problem, StringComparison.Ordinal);
        Assert.Equal(before, await StoredJsonAsync());
    }

    [Fact]
    public async Task Merges_again_when_another_writer_creates_the_blob_first()
    {
        // Our read finds nothing; another upload then creates the blob, so If-None-Match: * fails (409).
        var container = new InterferingContainer(this, interferences: 1);

        var result = await _store.WriteMergedAsync(container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(["Concorrente 1", "HD da Ana"], (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Merges_again_when_another_writer_changes_the_blob_after_our_read()
    {
        // The blob changes between our read and our write, so If-Match fails (412).
        await _store.WriteMergedAsync(_container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);
        var container = new InterferingContainer(this, interferences: 1);

        var result = await _store.WriteMergedAsync(container, Photo(Source("HD do Jonathas", "b.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(["Concorrente 1", "HD da Ana", "HD do Jonathas"], (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Gives_up_with_a_problem_when_every_attempt_conflicts()
    {
        var container = new InterferingContainer(this, interferences: int.MaxValue);

        var result = await _store.WriteMergedAsync(container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.Equal($"gave up after {ManifestStore.MaxAttempts} concurrent changes to the manifest in storage", result.Problem);
        Assert.DoesNotContain("HD da Ana", (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Erases_the_stored_faces_through_the_merge_and_then_has_nothing_to_write()
    {
        var erasedAt = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        await _store.WriteMergedAsync(
            _container,
            Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9)], FacesDetectedAt = erasedAt.AddDays(-1) },
            CancellationToken.None);

        var result = await _store.WriteMergedAsync(_container, new FacesErasure(PhotoId, erasedAt), CancellationToken.None);
        var again = await _store.WriteMergedAsync(_container, new FacesErasure(PhotoId, erasedAt), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(ManifestWriteStatus.Unchanged, again.Status);
        var stored = await StoredAsync();
        Assert.False(stored.FacesProcessed);
        Assert.Empty(stored.Faces);
        Assert.Equal(erasedAt, stored.FacesErasedAt);
        Assert.DoesNotContain("\"embedding\":", await StoredJsonAsync(), StringComparison.Ordinal);
        Assert.Equal(["HD da Ana"], stored.Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Creates_nothing_when_erasing_a_manifest_that_is_not_stored()
    {
        var result = await _store.WriteMergedAsync(_container, new FacesErasure(PhotoId, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Unchanged, result.Status);
        Assert.False(await Blob().ExistsAsync());
    }

    [Fact]
    public async Task Keeps_an_upload_that_lands_between_the_erase_s_read_and_write()
    {
        await _store.WriteMergedAsync(
            _container, Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9)] }, CancellationToken.None);
        var container = new InterferingContainer(this, interferences: 1);

        var result = await _store.WriteMergedAsync(container, new FacesErasure(PhotoId, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        var stored = await StoredAsync();
        Assert.Equal(["Concorrente 1", "HD da Ana"], stored.Sources.Select(s => s.Name));
        Assert.Empty(stored.Faces);
    }

    private BlobClient Blob() => _container.GetBlobClient(BlobLayout.ManifestBlobName(PhotoId));

    private async Task<string> StoredJsonAsync() => (await Blob().DownloadContentAsync()).Value.Content.ToString();

    private async Task<Manifest> StoredAsync() => ManifestJson.Deserialize(await StoredJsonAsync());

    /// <summary>The real validator, except that it refuses any manifest with more than one source.</summary>
    private sealed class RejectsSeveralSources : IManifestValidator
    {
        private readonly ManifestValidator _real = new();

        public bool TryValidate(string json, [NotNullWhen(true)] out Manifest? manifest, [NotNullWhen(false)] out string? problem)
        {
            if (!_real.TryValidate(json, out manifest, out problem))
            {
                return false;
            }

            if (manifest.Sources.Count > 1)
            {
                manifest = null;
                problem = "more than one source";
                return false;
            }

            return true;
        }
    }

    /// <summary>The manifests container, handing out the interfering blob for the test photo.</summary>
    private sealed class InterferingContainer(ManifestStoreTests test, int interferences)
        : BlobContainerClient(test._azurite.GetConnectionString(), test._container.Name)
    {
        private readonly InterferingBlob _blob = new(test, interferences);

        public override BlobClient GetBlobClient(string blobName) =>
            blobName == BlobLayout.ManifestBlobName(PhotoId) ? _blob : base.GetBlobClient(blobName);
    }

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
