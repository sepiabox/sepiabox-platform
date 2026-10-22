using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Uploading;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Storage;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// <c>upload</c> on the in-memory store (S-051): what it asks of <see cref="IBlobStore"/>, without Docker. The same
/// behaviour on Azure Blob Storage is in <see cref="UploadCommandTests"/>.
/// </summary>
public sealed class UploadToStoreTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();
    private readonly InMemoryBlobStore _store = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public async Task Commits_nothing_and_reports_the_file_when_it_changed_after_process()
    {
        var path = _drive.Write("a.jpg", TestPhotos.Porto("red"));
        _drive.Write("b.jpg", TestPhotos.Porto("green"));
        var changedId = Process().Single(manifest => manifest.Sources[0].Path == "a.jpg").Id;
        var content = File.ReadAllBytes(path);
        content[^3] ^= 0xFF; // same size, different bytes
        File.WriteAllBytes(path, content);

        var result = await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Local), _store);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(path, problem.Path);
        Assert.Null(await _store.ReadAsync(BlobLayout.OriginalsContainer, changedId));
        Assert.Null(await _store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(changedId)));
    }

    [Fact]
    public async Task Asks_for_the_cold_class_for_originals_only_when_the_destination_is_azure()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var id = Process().Single().Id;

        await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Azure, "account"), _store);

        Assert.Equal(BlobStorageClass.Cold, _store.StorageClassOf(BlobLayout.OriginalsContainer, id));
        Assert.All(
            await ToListAsync(_store.ListAsync(BlobLayout.DerivativesContainer)),
            derivative => Assert.Equal(BlobStorageClass.Default, _store.StorageClassOf(BlobLayout.DerivativesContainer, derivative.Name)));
    }

    [Fact]
    public async Task Keeps_the_default_class_for_a_local_destination_and_the_original_content_type()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var manifest = Process().Single();

        await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Local), _store);

        Assert.Equal(BlobStorageClass.Default, _store.StorageClassOf(BlobLayout.OriginalsContainer, manifest.Id));
        Assert.Equal(manifest.ContentType, _store.ContentTypeOf(BlobLayout.OriginalsContainer, manifest.Id));
        Assert.Equal(manifest.SizeBytes, await _store.GetSizeAsync(BlobLayout.OriginalsContainer, manifest.Id));
    }

    [Fact]
    public async Task Refuses_a_computer_clock_that_is_far_from_the_stores()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        Process();

        var failure = await Assert.ThrowsAsync<ClockSkewException>(() => UploadCommand.RunAsync(
            new UploadOptions(_work.Path, UploadDestination.Local), _store, null, new ManifestValidator(), CancellationToken.None, new Ahead(TimeSpan.FromMinutes(20))));

        Assert.True(failure.Skew > TimeSpan.FromMinutes(19));
        Assert.Null(await _store.ReadAsync(BlobLayout.OriginalsContainer, "anything"));
    }

    private List<Manifest> Process()
    {
        // No FFmpeg in these tests: photos get thumbnails, videos stay pending with no derivatives.
        ProcessCommand.Run(new ProcessOptions(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None));
        return [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json")
            .Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];
    }

    private static async Task<List<BlobListing>> ToListAsync(IAsyncEnumerable<BlobListing> listing)
    {
        var all = new List<BlobListing>();
        await foreach (var blob in listing)
        {
            all.Add(blob);
        }

        return all;
    }

    private sealed class Ahead(TimeSpan by) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + by;
    }
}
