using Azure.Storage.Blobs;
using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Uploading;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Tests.Shared;
using Testcontainers.Azurite;

namespace Sepiabox.Ingest.Tests;

// Each test gets its own Azurite so blobs left by one test never make another skip an upload.
[Trait("Category", "Integration")]
public sealed class UploadCommandTests : IAsyncLifetime, IDisposable
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        .WithInMemoryPersistence()
        // The storage SDK speaks a newer API version than Azurite announces; Aspire's emulator does the same.
        .WithCommand("--skipApiVersionCheck")
        .Build();

    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    private BlobServiceClient Service => new(_azurite.GetConnectionString());

    public Task InitializeAsync() => _azurite.StartAsync();

    public async Task DisposeAsync() => await _azurite.DisposeAsync();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public async Task Uploads_originals_derivatives_and_manifests_to_their_containers()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto("red"));
        _drive.Write("2019/serra.jpg", TestPhotos.Porto("green"));
        _drive.Write("backup/praia.jpg", TestPhotos.Porto("red"));
        var manifests = Process();

        var result = await Upload();

        Assert.Empty(result.Problems);
        Assert.Equal(2, result.Assets);
        Assert.Equal(8, result.Uploaded);
        var ids = manifests.Select(manifest => manifest.Id).Order().ToList();
        Assert.Equal(ids, await BlobNames(UploadCommand.OriginalsContainer));
        Assert.Equal(
            ids.SelectMany(id => new[] { $"{id}/preview-1280.webp", $"{id}/thumb-256.webp" }),
            await BlobNames(UploadCommand.DerivativesContainer));
        Assert.Equal(ids.Select(id => id + ".json"), await BlobNames(UploadCommand.ManifestsContainer));

        var original = Service.GetBlobContainerClient(UploadCommand.OriginalsContainer).GetBlobClient(ids[0]);
        var properties = (await original.GetPropertiesAsync()).Value;
        Assert.Equal("image/jpeg", properties.ContentType);
        Assert.Equal(manifests.Single(m => m.Id == ids[0]).SizeBytes, properties.ContentLength);
    }

    [Fact]
    public async Task Uploads_nothing_again_when_run_twice()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        _drive.Write("b.jpg", TestPhotos.Porto("green"));
        Process();
        await Upload();

        var second = await Upload();

        Assert.Equal(0, second.Uploaded);
        Assert.Equal(8, second.Skipped);
        Assert.Equal(0, second.Bytes);
    }

    [Fact]
    public async Task Continues_after_a_cancellation_without_uploading_anything_twice()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        // Larger than one 8 MB block, so the cancellation lands in the middle of this original.
        _drive.Write("video.mov", RandomBytes((UploadCommand.BlockSize * 3 / 2) + 123));
        Process();

        using var cancellation = new CancellationTokenSource();
        var cancelOnFirstBlock = new SyncProgress(bytes =>
        {
            if (bytes >= UploadCommand.BlockSize)
            {
                cancellation.Cancel();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => UploadCommand.RunAsync(Options(), Service, cancelOnFirstBlock, cancellation.Token));
        var beforeResume = ReadUploads();

        var resumed = await Upload();

        // Photo: original, two thumbnails, manifest. Video without FFmpeg: original and manifest.
        Assert.Empty(resumed.Problems);
        Assert.Equal(6, resumed.Uploaded + resumed.Skipped);
        Assert.True(resumed.Skipped >= beforeResume.Count);
        var afterResume = ReadUploads();
        Assert.Equal(6, afterResume.Count);
        // A blob uploaded again would have been given a new sequence number.
        Assert.All(beforeResume, record => Assert.Contains(record, afterResume));
        Assert.Equal(2, (await BlobNames(UploadCommand.OriginalsContainer)).Count);
        Assert.Equal(2, (await BlobNames(UploadCommand.ManifestsContainer)).Count);
    }

    [Fact]
    public async Task Reports_an_original_that_changed_after_process_and_does_not_upload_it()
    {
        var path = _drive.Write("a.jpg", TestPhotos.Porto("red"));
        _drive.Write("b.jpg", TestPhotos.Porto("green"));
        var changedId = Process().Single(manifest => manifest.Sources[0].Path == "a.jpg").Id;
        var content = File.ReadAllBytes(path);
        content[^3] ^= 0xFF; // same size, different bytes
        File.WriteAllBytes(path, content);

        var result = await Upload();

        var problem = Assert.Single(result.Problems);
        Assert.Equal(path, problem.Path);
        Assert.Contains("Changed since it was processed", problem.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(changedId, await BlobNames(UploadCommand.OriginalsContainer));
        Assert.DoesNotContain(changedId + ".json", await BlobNames(UploadCommand.ManifestsContainer));
        Assert.Single(await BlobNames(UploadCommand.ManifestsContainer));
    }

    [Fact]
    public async Task Reports_an_original_that_is_no_longer_on_the_drive()
    {
        var path = _drive.Write("a.jpg", TestPhotos.Porto("red"));
        Process();
        File.Delete(path);

        var result = await Upload();

        Assert.Contains("Original not found", Assert.Single(result.Problems).Reason, StringComparison.Ordinal);
        Assert.Empty(await BlobNames(UploadCommand.ManifestsContainer));
    }

    [Fact]
    public async Task Uploads_each_manifest_after_its_original_and_derivatives()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        _drive.Write("b.jpg", TestPhotos.Porto("green"));
        _drive.Write("c.jpg", TestPhotos.Porto("blue"));
        var manifests = Process();

        await Upload();

        var uploads = ReadUploads();
        foreach (var manifest in manifests)
        {
            var manifestSequence = uploads.Single(r => r.Container == UploadCommand.ManifestsContainer && r.Blob == manifest.Id + ".json").Sequence;
            var dependencies = uploads.Where(r =>
                (r.Container == UploadCommand.OriginalsContainer && r.Blob == manifest.Id)
                || (r.Container == UploadCommand.DerivativesContainer && r.Blob.StartsWith(manifest.Id + "/", StringComparison.Ordinal)))
                .ToList();
            Assert.Equal(3, dependencies.Count);
            Assert.All(dependencies, dependency => Assert.True(dependency.Sequence < manifestSequence));
        }
    }

    private List<Manifest> Process()
    {
        // No FFmpeg in these tests: photos get thumbnails, videos stay pending with no derivatives.
        ProcessCommand.Run(new ProcessOptions(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: Path.Combine(_work.Path, "no-ffmpeg")));
        return [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json")
            .Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];
    }

    private UploadOptions Options() => new(_work.Path, UploadDestination.Local);

    private Task<UploadResult> Upload() => UploadCommand.RunAsync(Options(), Service);

    private List<UploadRecord> ReadUploads()
    {
        using var state = new UploadState(Path.Combine(_work.Path, "state.db"));
        return [.. state.All()];
    }

    private async Task<List<string>> BlobNames(string container)
    {
        var client = Service.GetBlobContainerClient(container);
        if (!await client.ExistsAsync())
        {
            return [];
        }

        var names = new List<string>();
        await foreach (var blob in client.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }

        return [.. names.Order(StringComparer.Ordinal)];
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
