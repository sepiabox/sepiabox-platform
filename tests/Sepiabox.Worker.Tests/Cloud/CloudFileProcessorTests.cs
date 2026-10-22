using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Storage;
using Sepiabox.Worker.Cloud;

namespace Sepiabox.Worker.Tests.Cloud;

/// <summary>
/// <c>process --from-queue</c> for one file (S-036, D-107), on the in-memory store: no Docker, no FFmpeg. The same on Azurite
/// and with a real video is in <see cref="CloudFileProcessorIntegrationTests"/>.
/// </summary>
public sealed class CloudFileProcessorTests
{
    private static readonly DateTimeOffset s_modified = new(2019, 7, 14, 10, 30, 0, TimeSpan.Zero);

    private readonly InMemoryBlobStore _store = new();
    private readonly FakeEmbedder _embedder = new();

    public CloudFileProcessorTests()
    {
        foreach (var container in new[] { "incoming", "originals", "derivatives", "manifests" })
        {
            _store.CreateContainer(container);
        }
    }

    [Fact]
    public async Task Makes_the_manifest_and_derivatives_moves_the_original_and_empties_incoming_in_custody_mode()
    {
        var bytes = TestPhotos.Taken("2019:07:14 10:30:00");
        await Put("a1", bytes, File("Família/Praia 2019.jpg"));

        var result = await Processor().ProcessAsync("a1", CancellationToken.None);

        var id = MediaFile.Sha256Hex(bytes);
        Assert.Equal(new CloudFileResult(CloudFileStatus.Processed, AssetId: id), result);
        var manifest = await ManifestAsync(id);
        Assert.Equal((MediaKind.Photo, bytes.Length), (manifest.Kind, (int)manifest.SizeBytes));
        Assert.Equal(new ManifestSource("gdrive", "Família Costa", "Família/Praia 2019.jpg", s_modified), Assert.Single(manifest.Sources));
        Assert.Equal(new DateTime(2019, 7, 14, 10, 30, 0), manifest.CapturedAt);
        Assert.Equal(CaptureDateSource.Metadata, manifest.CapturedAtSource);
        Assert.True(manifest.IsOriginalStored);
        Assert.Equal(["preview-1280", "thumb-256"], manifest.Derivatives.Select(d => d.Name).Order());
        Assert.All(manifest.Derivatives, d => Assert.NotNull(_store.ReadAsync("derivatives", BlobLayout.DerivativeBlobName(id, d.Path)).Result));
        Assert.Contains(ClipKey, manifest.Embeddings.Keys);
        Assert.NotNull(manifest.Quality);
        Assert.Equal(bytes, (await _store.ReadAsync("originals", id))!.Content.ToArray());
        Assert.Null(await _store.ReadAsync("incoming", "a1"));
    }

    [Fact]
    public async Task Stores_no_original_in_reference_mode_and_makes_the_larger_previews()
    {
        var bytes = TestPhotos.Porto();
        await Put("r1", bytes, File("Fotos/a.jpg", IncomingMode.Reference, url: "https://drive.example.com/file/1"));

        await Processor().ProcessAsync("r1", CancellationToken.None);

        var id = MediaFile.Sha256Hex(bytes);
        Assert.Empty(await _store.ListAsync("originals").ToListAsync());
        var manifest = await ManifestAsync(id);
        Assert.False(manifest.IsOriginalStored);
        Assert.Equal("https://drive.example.com/file/1", Assert.Single(manifest.Sources).Url);
        Assert.Contains(manifest.Derivatives, d => d.Name == "preview-2048");
        Assert.Null(await _store.ReadAsync("incoming", "r1"));
    }

    [Fact]
    public async Task Dates_a_file_with_no_metadata_from_its_path_in_the_cloud_source()
    {
        await Put("p1", TestPhotos.Plain(), File("Fotos/2018-12-25/natal.jpg"));

        var result = await Processor().ProcessAsync("p1", CancellationToken.None);

        var manifest = await ManifestAsync(result.AssetId!);
        Assert.Equal(CaptureDateSource.Folder, manifest.CapturedAtSource);
        Assert.Equal(new DateTime(2018, 12, 25), manifest.CapturedAt);
    }

    [Fact]
    public async Task The_same_file_twice_is_one_asset_with_two_sources_and_its_derivatives_are_made_once()
    {
        var bytes = TestPhotos.Porto("red");
        await Put("one", bytes, File("Fotos/a.jpg"));
        await Put("two", bytes, File("Backup/a-copy.jpg"));

        var first = await Processor().ProcessAsync("one", CancellationToken.None);
        var second = await Processor().ProcessAsync("two", CancellationToken.None);

        Assert.Equal(CloudFileStatus.Processed, second.Status);
        var manifest = await ManifestAsync(first.AssetId!);
        Assert.Equal(["Backup/a-copy.jpg", "Fotos/a.jpg"], manifest.Sources.Select(s => s.Path).Order());
        Assert.Single(_embedder.Embedded);
        Assert.Single(await _store.ListAsync("originals").ToListAsync());
        Assert.Empty(await _store.ListAsync("incoming").ToListAsync());
    }

    [Fact]
    public async Task A_message_delivered_again_after_the_blob_is_gone_changes_nothing()
    {
        await Put("a1", TestPhotos.Porto(), File("a.jpg"));
        var processor = Processor();
        var first = await processor.ProcessAsync("a1", CancellationToken.None);
        var before = (await _store.ReadAsync("manifests", BlobLayout.ManifestBlobName(first.AssetId!)))!.Version;

        var again = await processor.ProcessAsync("a1", CancellationToken.None);

        Assert.Equal(CloudFileStatus.AlreadyDone, again.Status);
        Assert.Equal(before, (await _store.ReadAsync("manifests", BlobLayout.ManifestBlobName(first.AssetId!)))!.Version);
    }

    [Fact]
    public async Task The_same_blob_processed_twice_after_a_crash_before_the_delete_gives_the_same_result()
    {
        var bytes = TestPhotos.Porto("green");
        await Put("a1", bytes, File("a.jpg"));
        // A crash after the manifest was written but before the incoming blob was deleted: the blob is still there.
        await Processor().ProcessAsync("a1", CancellationToken.None);
        await Put("a1", bytes, File("a.jpg"));

        var result = await Processor().ProcessAsync("a1", CancellationToken.None);

        var manifest = await ManifestAsync(result.AssetId!);
        Assert.Single(manifest.Sources);
        Assert.Equal(2, manifest.Derivatives.Count);
        Assert.Null(await _store.ReadAsync("incoming", "a1"));
    }

    [Fact]
    public async Task Leaves_out_a_file_that_is_not_a_photo_or_video_without_a_manifest()
    {
        await Put("n1", "not media"u8.ToArray(), File("Docs/notes.txt"));

        var result = await Processor().ProcessAsync("n1", CancellationToken.None);

        Assert.Equal(CloudFileStatus.LeftOut, result.Status);
        Assert.Empty(await _store.ListAsync("manifests").ToListAsync());
        Assert.Empty(await _store.ListAsync("originals").ToListAsync());
        Assert.Null(await _store.ReadAsync("incoming", "n1"));
    }

    [Fact]
    public async Task Reports_a_blob_without_usable_metadata_and_leaves_it_in_incoming()
    {
        await _store.WriteAsync("incoming", "x1", BinaryData.FromBytes(TestPhotos.Porto()), "image/jpeg", BlobWriteCondition.Always, new Dictionary<string, string> { ["path"] = "a.jpg" });

        var result = await Processor().ProcessAsync("x1", CancellationToken.None);

        Assert.Equal(CloudFileStatus.Problem, result.Status);
        Assert.Contains("sourcetype", result.Reason, StringComparison.Ordinal);
        Assert.NotNull(await _store.ReadAsync("incoming", "x1"));
        Assert.Empty(await _store.ListAsync("manifests").ToListAsync());
    }

    [Fact]
    public async Task Reports_a_photo_that_cannot_be_decoded_and_leaves_it_in_incoming()
    {
        await Put("c1", Enumerable.Range(0, 2_000).Select(i => (byte)(i * 31 % 251)).ToArray(), File("broken.jpg"));

        var result = await Processor().ProcessAsync("c1", CancellationToken.None);

        Assert.Equal(CloudFileStatus.Problem, result.Status);
        Assert.Contains("thumbnails", result.Reason, StringComparison.Ordinal);
        Assert.NotNull(await _store.ReadAsync("incoming", "c1"));
        Assert.Empty(await _store.ListAsync("manifests").ToListAsync());
        Assert.Empty(await _store.ListAsync("originals").ToListAsync());
    }

    [Fact]
    public async Task A_video_without_ffmpeg_fails_the_message_so_it_is_delivered_again()
    {
        await Put("v1", [0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70], File("Clips/a.mp4"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Processor().ProcessAsync("v1", CancellationToken.None));

        Assert.NotNull(await _store.ReadAsync("incoming", "v1"));
    }

    [Fact]
    public async Task Looks_for_faces_only_with_a_detector_and_records_when()
    {
        var detector = new FakeFaceDetector();
        await Put("f1", TestPhotos.Porto("blue"), File("a.jpg"));
        await Put("f2", TestPhotos.Porto("pink"), File("b.jpg"));

        var with = await Processor(detector).ProcessAsync("f1", CancellationToken.None);
        var without = await Processor().ProcessAsync("f2", CancellationToken.None);

        var found = await ManifestAsync(with.AssetId!);
        Assert.True(found.FacesProcessed);
        Assert.Single(found.Faces);
        Assert.NotNull(found.FacesDetectedAt);
        Assert.False((await ManifestAsync(without.AssetId!)).FacesProcessed);
    }

    [Fact]
    public async Task Leaves_out_the_embedding_and_goes_on_when_it_fails()
    {
        _embedder.Failure = _ => new Sepiabox.Pipeline.Embeddings.EmbeddingException("model broke");
        await Put("e1", TestPhotos.Porto("gold"), File("a.jpg"));

        var result = await Processor().ProcessAsync("e1", CancellationToken.None);

        Assert.Equal(CloudFileStatus.Processed, result.Status);
        Assert.Empty((await ManifestAsync(result.AssetId!)).Embeddings);
    }

    [Theory]
    [InlineData(CloudFileProcessor.BlockSize - 1, false)]
    [InlineData(CloudFileProcessor.BlockSize, true)]
    [InlineData((CloudFileProcessor.BlockSize * 2) + 5, true)]
    public async Task Writes_a_derivative_of_less_than_a_block_whole_and_stages_a_larger_one(int size, bool staged)
    {
        var counting = new CountingStore(_store);
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "proxy.mp4");
        var bytes = new byte[size];
        new Random(3).NextBytes(bytes);
        await System.IO.File.WriteAllBytesAsync(path, bytes);
        var processor = new CloudFileProcessor(counting, new ManifestStore(new ManifestValidator(), counting), null, null, null, TimeProvider.System, NullLogger.Instance);

        await processor.WriteDerivativeAsync("abc/proxy-720p.mp4", path, "video/mp4", CancellationToken.None);

        Assert.Equal(staged, counting.Staged);
        Assert.Equal(!staged, counting.Written);
        Assert.Equal(bytes, (await _store.ReadAsync("derivatives", "abc/proxy-720p.mp4"))!.Content.ToArray());
    }

    private sealed class CountingStore(IBlobStore inner) : DelegatingBlobStore(inner)
    {
        public bool Staged { get; private set; }

        public bool Written { get; private set; }

        public override IStagedUpload BeginUpload(string container, string name)
        {
            Staged = true;
            return base.BeginUpload(container, name);
        }

        public override Task<BlobWriteResult> WriteAsync(
            string container, string name, BinaryData content, string contentType, BlobWriteCondition condition, CancellationToken cancellationToken = default)
        {
            Written = true;
            return base.WriteAsync(container, name, content, contentType, condition, cancellationToken);
        }
    }

    private static string ClipKey => Sepiabox.Pipeline.Embeddings.ClipImageEmbedder.ModelKey;

    private static IncomingFile File(string path, IncomingMode mode = IncomingMode.Custody, string? url = null) =>
        new("gdrive", "Família Costa", path, mode, s_modified, ExternalId: "id-" + path.GetHashCode(StringComparison.Ordinal), Url: url);

    private Task<BlobWriteResult> Put(string name, byte[] bytes, IncomingFile file) =>
        _store.WriteAsync("incoming", name, BinaryData.FromBytes(bytes), "application/octet-stream", BlobWriteCondition.Always, file.ToMetadata());

    private CloudFileProcessor Processor(Sepiabox.Pipeline.Faces.IFaceDetector? faces = null) =>
        new(_store, new ManifestStore(new ManifestValidator(), _store), video: null, _embedder, faces, TimeProvider.System, NullLogger.Instance);

    private async Task<Manifest> ManifestAsync(string id) =>
        ManifestJson.Deserialize((await _store.ReadAsync("manifests", BlobLayout.ManifestBlobName(id)))!.Content.ToString());
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var all = new List<T>();
        await foreach (var item in source)
        {
            all.Add(item);
        }

        return all;
    }
}
