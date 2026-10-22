using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Storage;
using Sepiabox.Storage.Azure;
using Testcontainers.Azurite;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Pipeline.Tests;

/// <summary>
/// <c>ManifestStore</c> (D-036) on any <see cref="IBlobStore"/> (S-050): the same tests run on the in-memory store and on
/// Azurite, so the merge, the skip when nothing changes and the retries do not depend on which one is behind them.
/// </summary>
public abstract class ManifestStoreTests
{
    protected const string Container = "manifests";

    /// <summary>The store the manifests are in.</summary>
    protected abstract IBlobStore Backing { get; }

    private ManifestStore Store => new(new ManifestValidator(), Backing);

    [Fact]
    public async Task Writes_a_new_manifest_in_its_stored_form()
    {
        var manifest = Photo(Source("HD da Ana", "2019\\praia.jpg"));

        var result = await Store.WriteMergedAsync(Container, manifest, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(ManifestJson.Serialize(ManifestMerge.Canonical(manifest)), await StoredJsonAsync());
    }

    [Fact]
    public async Task Names_the_blob_after_the_manifest_id()
    {
        var other = Photo(Source("HD da Ana", "a.jpg")) with { Id = new string('c', 64) };

        await Store.WriteMergedAsync(Container, other, CancellationToken.None);

        Assert.NotNull(await Backing.ReadAsync(Container, BlobLayout.ManifestBlobName(other.Id)));
        Assert.Null(await Backing.ReadAsync(Container, BlobLayout.ManifestBlobName(PhotoId)));
    }

    [Fact]
    public async Task Merges_into_the_stored_manifest()
    {
        await Store.WriteMergedAsync(Container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        var result = await Store.WriteMergedAsync(Container, Photo(Source("HD do Jonathas", "b.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(["HD da Ana", "HD do Jonathas"], (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Does_not_write_when_nothing_would_change()
    {
        var manifest = Photo(Source("HD da Ana", "a.jpg"));
        await Store.WriteMergedAsync(Container, manifest, CancellationToken.None);
        var version = await VersionAsync();

        var result = await Store.WriteMergedAsync(Container, manifest, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Unchanged, result.Status);
        Assert.Equal(version, await VersionAsync());
    }

    [Fact]
    public async Task Does_not_write_again_when_the_same_faces_are_uploaded()
    {
        var manifest = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9)] };
        await Store.WriteMergedAsync(Container, manifest, CancellationToken.None);
        var version = await VersionAsync();

        var reread = ManifestJson.Deserialize(ManifestJson.Serialize(manifest));
        var result = await Store.WriteMergedAsync(Container, reread, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Unchanged, result.Status);
        Assert.Equal(version, await VersionAsync());
    }

    [Fact]
    public async Task Uploads_of_different_faces_from_two_computers_settle_on_one_blob()
    {
        var ana = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.91)] };
        var jonathas = Photo(Source("HD do Jonathas", "b.jpg")) with { FacesProcessed = true, Faces = [Face(0.93, axis: 1)] };
        await Store.WriteMergedAsync(Container, ana, CancellationToken.None);
        await Store.WriteMergedAsync(Container, jonathas, CancellationToken.None);
        var version = await VersionAsync();

        Assert.Equal(ManifestWriteStatus.Unchanged, (await Store.WriteMergedAsync(Container, ana, CancellationToken.None)).Status);
        Assert.Equal(ManifestWriteStatus.Unchanged, (await Store.WriteMergedAsync(Container, jonathas, CancellationToken.None)).Status);
        Assert.Equal(version, await VersionAsync());
    }

    [Fact]
    public async Task A_stored_manifest_that_cannot_be_read_is_a_problem_and_is_left_alone()
    {
        await PutAsync("{ \"id\": 42 }");

        var result = await Store.WriteMergedAsync(Container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("the manifest in storage cannot be read", result.Problem, StringComparison.Ordinal);
        Assert.Equal("{ \"id\": 42 }", await StoredJsonAsync());
    }

    [Fact]
    public async Task A_stored_manifest_of_another_asset_is_a_problem()
    {
        var other = Photo(Source("HD da Ana", "a.jpg")) with { Id = new string('b', 64) };
        await PutAsync(ManifestJson.Serialize(other));

        var result = await Store.WriteMergedAsync(Container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.Contains(other.Id, result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unusable_work_folder_manifest_is_a_problem_and_creates_nothing()
    {
        var incoming = Photo(Source("HD da Ana", "a.jpg")) with { Sources = null! };

        var result = await Store.WriteMergedAsync(Container, incoming, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("this manifest is not valid", result.Problem, StringComparison.Ordinal);
        Assert.Null(await Backing.ReadAsync(Container, BlobLayout.ManifestBlobName(PhotoId)));
    }

    [Fact]
    public async Task An_invalid_incoming_manifest_is_a_problem_and_leaves_the_stored_one_alone()
    {
        await Store.WriteMergedAsync(Container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);
        var before = await StoredJsonAsync();

        // Width 0 deserializes fine but the schema requires at least 1.
        var result = await Store.WriteMergedAsync(Container, Photo(Source("HD do Jonathas", "b.jpg")) with { Width = 0 }, CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("this manifest is not valid", result.Problem, StringComparison.Ordinal);
        Assert.Equal(before, await StoredJsonAsync());
    }

    [Fact]
    public async Task A_merge_that_would_store_an_invalid_manifest_is_a_problem_and_writes_nothing()
    {
        // Two valid manifests cannot merge into an invalid one under today's schema, so a validator
        // that refuses any manifest with more than one source stands in for a future rule that could.
        var store = new ManifestStore(new RejectsSeveralSources(), Backing);
        await store.WriteMergedAsync(Container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);
        var before = await StoredJsonAsync();

        var result = await store.WriteMergedAsync(Container, Photo(Source("HD do Jonathas", "b.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.StartsWith("the merged manifest is not valid: more than one source", result.Problem, StringComparison.Ordinal);
        Assert.Equal(before, await StoredJsonAsync());
    }

    [Fact]
    public async Task Merges_again_when_another_writer_creates_the_blob_first()
    {
        // Our read finds nothing; another upload then creates the blob, so If-None-Match: * fails (409).
        var store = Interfering(interferences: 1);

        var result = await store.WriteMergedAsync(Container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(["Concorrente 1", "HD da Ana"], (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Merges_again_when_another_writer_changes_the_blob_after_our_read()
    {
        // The blob changes between our read and our write, so If-Match fails (412).
        await Store.WriteMergedAsync(Container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);
        var store = Interfering(interferences: 1);

        var result = await store.WriteMergedAsync(Container, Photo(Source("HD do Jonathas", "b.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(["Concorrente 1", "HD da Ana", "HD do Jonathas"], (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Gives_up_with_a_problem_when_every_attempt_conflicts()
    {
        var store = Interfering(interferences: int.MaxValue);

        var result = await store.WriteMergedAsync(Container, Photo(Source("HD da Ana", "a.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Problem, result.Status);
        Assert.Equal($"gave up after {ManifestStore.MaxAttempts} concurrent changes to the manifest in storage", result.Problem);
        Assert.DoesNotContain("HD da Ana", (await StoredAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Erases_the_stored_faces_through_the_merge_and_then_has_nothing_to_write()
    {
        var erasedAt = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        await Store.WriteMergedAsync(
            Container,
            Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9)], FacesDetectedAt = erasedAt.AddDays(-1) },
            CancellationToken.None);

        var result = await Store.WriteMergedAsync(Container, new FacesErasure(PhotoId, erasedAt), CancellationToken.None);
        var again = await Store.WriteMergedAsync(Container, new FacesErasure(PhotoId, erasedAt), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        Assert.Equal(ManifestWriteStatus.Unchanged, again.Status);
        var stored = await StoredAsync();
        Assert.False(stored.FacesProcessed);
        Assert.Empty(stored.Faces);
        Assert.Equal(erasedAt, stored.FacesErasedAt);
        Assert.DoesNotContain("\"embedding\":", await StoredJsonAsync(), StringComparison.Ordinal);
        Assert.Equal(["HD da Ana"], stored.Sources.Select(s => s.Name));
    }

    [Theory]
    [InlineData(24 * 60, false)]
    [InlineData(7, false)]
    [InlineData(3, true)]
    public async Task Drops_stored_faces_stamped_by_a_clock_ahead_when_another_upload_merges_before_the_sweep(int minutesAhead, bool kept)
    {
        // An older upload stored faces found by a computer whose clock was ahead when it ran process; then recognition
        // was turned off, and another computer uploads the same photo before the sweep reaches the blob.
        await Store.WriteMergedAsync(
            Container,
            Photo(Source("HD da Ana", "a.jpg")) with
            {
                FacesProcessed = true,
                Faces = [Face(0.9)],
                FacesDetectedAt = DateTimeOffset.UtcNow.AddMinutes(minutesAhead),
            },
            CancellationToken.None);

        var result = await Store.WriteMergedAsync(Container, Photo(Source("HD do Jonathas", "b.jpg")), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        var stored = await StoredAsync();
        Assert.Equal(kept, stored.FacesProcessed);
        Assert.Equal(kept ? 1 : 0, stored.Faces.Count);
        Assert.Equal(["HD da Ana", "HD do Jonathas"], stored.Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Creates_nothing_when_erasing_a_manifest_that_is_not_stored()
    {
        var result = await Store.WriteMergedAsync(Container, new FacesErasure(PhotoId, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Unchanged, result.Status);
        Assert.Null(await Backing.ReadAsync(Container, BlobLayout.ManifestBlobName(PhotoId)));
    }

    [Fact]
    public async Task Keeps_an_upload_that_lands_between_the_erase_s_read_and_write()
    {
        await Store.WriteMergedAsync(
            Container, Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9)] }, CancellationToken.None);
        var store = Interfering(interferences: 1);

        var result = await store.WriteMergedAsync(Container, new FacesErasure(PhotoId, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(ManifestWriteStatus.Written, result.Status);
        var stored = await StoredAsync();
        Assert.Equal(["Concorrente 1", "HD da Ana"], stored.Sources.Select(s => s.Name));
        Assert.Empty(stored.Faces);
    }

    private async Task PutAsync(string json) =>
        await Backing.WriteAsync(Container, BlobLayout.ManifestBlobName(PhotoId), BinaryData.FromString(json), "application/json", BlobWriteCondition.Always);

    private async Task<string?> VersionAsync() => (await Backing.ReadAsync(Container, BlobLayout.ManifestBlobName(PhotoId)))?.Version;

    private async Task<string> StoredJsonAsync() =>
        (await Backing.ReadAsync(Container, BlobLayout.ManifestBlobName(PhotoId)))!.Content.ToString();

    private async Task<Manifest> StoredAsync() => ManifestJson.Deserialize(await StoredJsonAsync());

    private ManifestStore Interfering(int interferences) => new(new ManifestValidator(), new InterferingStore(this, interferences));

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

    /// <summary>
    /// The backing store, except that another upload of the same asset writes the manifest right after each of our reads of
    /// it, for the first <c>interferences</c> reads: our write then finds the blob created (If-None-Match) or changed (If-Match).
    /// </summary>
    private sealed class InterferingStore(ManifestStoreTests test, int interferences) : IBlobStore
    {
        private int _written;

        private IBlobStore Inner => test.Backing;

        public bool IsStoreFailure(Exception exception) => Inner.IsStoreFailure(exception);

        public Task CreateContainerAsync(string container, CancellationToken cancellationToken = default) =>
            Inner.CreateContainerAsync(container, cancellationToken);

        public Task<long?> GetSizeAsync(string container, string name, CancellationToken cancellationToken = default) =>
            Inner.GetSizeAsync(container, name, cancellationToken);

        public Task<DateTimeOffset?> GetServerTimeAsync(string container, CancellationToken cancellationToken = default) =>
            Inner.GetServerTimeAsync(container, cancellationToken);

        public IStagedUpload BeginUpload(string container, string name) => Inner.BeginUpload(container, name);

        public Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default) =>
            Inner.ContainerExistsAsync(container, cancellationToken);

        public async Task<BlobContent?> ReadAsync(string container, string name, CancellationToken cancellationToken = default)
        {
            var read = await Inner.ReadAsync(container, name, cancellationToken);
            if (name == BlobLayout.ManifestBlobName(PhotoId) && _written < interferences)
            {
                _written++;
                var competitor = Photo(Source($"Concorrente {_written}", "c.jpg"));
                var stored = read is null ? competitor : ManifestMerge.Merge(ManifestJson.Deserialize(read.Content.ToString()), competitor);
                await Inner.WriteAsync(container, name, BinaryData.FromString(ManifestJson.Serialize(stored)), "application/json", BlobWriteCondition.Always, cancellationToken);
            }

            return read;
        }

        public IAsyncEnumerable<BlobListing> ListAsync(string container, string? prefix = null, CancellationToken cancellationToken = default) =>
            Inner.ListAsync(container, prefix, cancellationToken);

        public Task<BlobPage> ListPageAsync(string container, string? prefix, string? continuationToken, int pageSize, CancellationToken cancellationToken = default) =>
            Inner.ListPageAsync(container, prefix, continuationToken, pageSize, cancellationToken);

        public Task<BlobWriteResult> WriteAsync(
            string container, string name, BinaryData content, string contentType, BlobWriteCondition condition, CancellationToken cancellationToken = default) =>
            Inner.WriteAsync(container, name, content, contentType, condition, cancellationToken);

        public Task<bool> DeleteAsync(string container, string name, CancellationToken cancellationToken = default) =>
            Inner.DeleteAsync(container, name, cancellationToken);

        public Task<Uri> CreateReadUrlAsync(
            string container, string name, DateTimeOffset expiresOn, BlobReadUrlOptions? options = null, CancellationToken cancellationToken = default) =>
            Inner.CreateReadUrlAsync(container, name, expiresOn, options, cancellationToken);
    }
}

/// <summary>On <see cref="InMemoryBlobStore"/>.</summary>
public sealed class ManifestStoreInMemoryTests : ManifestStoreTests
{
    protected override IBlobStore Backing { get; } = new InMemoryBlobStore();
}

/// <summary>On Azure Blob Storage, as Azurite emulates it.</summary>
[Trait("Category", "Integration")]
public sealed class ManifestStoreAzuriteTests : ManifestStoreTests, IAsyncLifetime, IDisposable
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        .WithInMemoryPersistence()
        // The storage SDK speaks a newer API version than Azurite announces; Aspire's emulator does the same.
        .WithCommand("--skipApiVersionCheck")
        .Build();

    private AzureBlobStore? _backing;

    protected override IBlobStore Backing => _backing ?? throw new InvalidOperationException("Azurite is not started.");

    public async Task InitializeAsync()
    {
        await _azurite.StartAsync();
        var service = new BlobServiceClient(_azurite.GetConnectionString());
        await service.GetBlobContainerClient(Container).CreateAsync();
        _backing = new AzureBlobStore(service);
    }

    public async Task DisposeAsync() => await _azurite.DisposeAsync();

    public void Dispose() => _backing?.Dispose();
}
