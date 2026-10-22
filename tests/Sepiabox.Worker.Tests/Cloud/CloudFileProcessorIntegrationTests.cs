using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Pipeline.Video;
using Sepiabox.Storage;
using Sepiabox.Worker.Cloud;
using Sepiabox.Worker.Events;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Tests.Cloud;

/// <summary>
/// <c>process --from-queue</c> on Azurite and PostgreSQL, from the queue's event to the catalog (S-036, D-107), with the real
/// FFmpeg for a video.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class CloudFileProcessorIntegrationTests(CatalogFixture fixture)
{
    [Fact]
    public async Task A_blob_in_incoming_becomes_a_manifest_and_derivatives_and_is_gone_and_the_manifest_imports()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await CreateContainersAsync();
        var bytes = TestPhotos.Taken("2019:07:14 10:30:00", "plum");
        var name = "azurite-" + Guid.NewGuid().ToString("N");
        await PutAsync(name, bytes, new IncomingFile("gdrive", "Família", "Fotos/praia.jpg", IncomingMode.Custody, new DateTimeOffset(2019, 7, 14, 10, 30, 0, TimeSpan.Zero)));
        var queue = new InMemoryBlobEventQueue(new BlobCreated(BlobLayout.IncomingContainer, name), new BlobCreated(BlobLayout.IncomingContainer, name));

        var result = await new QueueImport(
                queue,
                async (blob, token) =>
                {
                    var done = await Processor(video: null).ProcessAsync(blob, token);
                    return new ImportResult(done.Status == CloudFileStatus.Processed ? 1 : 0, 0, [], []);
                },
                QueueImportOptions.Default with { IdleWait = TimeSpan.Zero },
                NullLogger<QueueImport>.Instance,
                BlobLayout.IncomingContainer)
            .RunAsync(CancellationToken.None);

        // The second event is the same message delivered again: the blob is gone, nothing changes.
        Assert.Equal(1, result.Imported);
        Assert.Equal(["message-0", "message-1"], queue.Completed);
        var id = MediaFile.Sha256Hex(bytes);
        Assert.Null(await fixture.Store.ReadAsync(BlobLayout.IncomingContainer, name));
        Assert.NotNull(await fixture.Store.ReadAsync(BlobLayout.OriginalsContainer, id));
        var manifest = ManifestJson.Deserialize((await fixture.Store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(id)))!.Content.ToString());
        Assert.Equal(2, manifest.Derivatives.Count);
        Assert.All(manifest.Derivatives, d => Assert.NotNull(fixture.Store.ReadAsync(BlobLayout.DerivativesContainer, BlobLayout.DerivativeBlobName(id, d.Path)).Result));

        // And the import takes it from there, as for a manifest uploaded from a PC.
        var imported = await new ManifestImporter(
                db, fixture.Store, new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOneAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(id), CancellationToken.None);
        Assert.Equal(1, imported.Imported);
        var asset = await db.Assets.AsNoTracking().Include(a => a.Sources).SingleAsync(a => a.Id == id);
        Assert.Equal(("gdrive", "Família", "Fotos/praia.jpg"), (asset.Sources[0].Type, asset.Sources[0].Name, asset.Sources[0].Path));
    }

    [Fact]
    public async Task Reference_mode_writes_nothing_to_originals_on_azurite()
    {
        await CreateContainersAsync();
        var bytes = TestPhotos.Porto("salmon");
        var name = "reference-" + Guid.NewGuid().ToString("N");
        await PutAsync(name, bytes, new IncomingFile("onedrive", "Ana", "Pictures/a.jpg", IncomingMode.Reference));

        var result = await Processor(video: null).ProcessAsync(name, CancellationToken.None);

        Assert.Equal(CloudFileStatus.Processed, result.Status);
        Assert.Null(await fixture.Store.ReadAsync(BlobLayout.OriginalsContainer, MediaFile.Sha256Hex(bytes)));
        Assert.Null(await fixture.Store.ReadAsync(BlobLayout.IncomingContainer, name));
    }

    [Fact]
    public async Task A_video_gets_its_poster_and_720p_version_through_the_real_ffmpeg()
    {
        var tools = FfmpegTools.Find(null) ?? throw new InvalidOperationException("Integration tests need ffmpeg and ffprobe on the PATH.");
        await CreateContainersAsync();
        using var folder = new TempFolder();
        var clip = Path.Combine(folder.Path, "clip.mp4");
        Run(tools.Ffmpeg, "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=320x240:rate=25", "-f", "lavfi", "-i", "sine=frequency=330:duration=1",
            "-c:v", "libx264", "-c:a", "aac", "-shortest", clip);
        var bytes = await File.ReadAllBytesAsync(clip);
        var name = "video-" + Guid.NewGuid().ToString("N");
        await PutAsync(name, bytes, new IncomingFile("gdrive", "Família", "Clips/mar.mp4", IncomingMode.Custody));

        var result = await Processor(new VideoProcessor(tools)).ProcessAsync(name, CancellationToken.None);

        Assert.Equal(CloudFileStatus.Processed, result.Status);
        var manifest = ManifestJson.Deserialize((await fixture.Store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(result.AssetId!)))!.Content.ToString());
        Assert.Equal(MediaKind.Video, manifest.Kind);
        Assert.Equal(["poster", "proxy-720p", "thumb-256"], manifest.Derivatives.Select(d => d.Name).Order());
        Assert.All(manifest.Derivatives, d => Assert.NotNull(fixture.Store.ReadAsync(BlobLayout.DerivativesContainer, BlobLayout.DerivativeBlobName(result.AssetId!, d.Path)).Result));
        Assert.NotNull(await fixture.Store.ReadAsync(BlobLayout.OriginalsContainer, result.AssetId!));
    }

    [Fact]
    public async Task A_video_in_reference_mode_gets_the_1080p_version_and_no_original()
    {
        var tools = FfmpegTools.Find(null) ?? throw new InvalidOperationException("Integration tests need ffmpeg and ffprobe on the PATH.");
        await CreateContainersAsync();
        using var folder = new TempFolder();
        var clip = Path.Combine(folder.Path, "clip2.mp4");
        Run(tools.Ffmpeg, "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=duration=1:size=320x240:rate=25",
            "-c:v", "libx264", clip);
        var bytes = await File.ReadAllBytesAsync(clip);
        var name = "videoref-" + Guid.NewGuid().ToString("N");
        await PutAsync(name, bytes, new IncomingFile("onedrive", "Ana", "Videos/mar.mp4", IncomingMode.Reference));
        var processor = new CloudFileProcessor(
            fixture.Store, new ManifestStore(new ManifestValidator(), fixture.Store), new VideoProcessor(tools), embedder: null, faces: null,
            TimeProvider.System, NullLogger.Instance, referenceVideo: new VideoProcessor(tools, VideoEncoder.Software, VideoVersion.Hd1080));

        var result = await processor.ProcessAsync(name, CancellationToken.None);

        Assert.Equal(CloudFileStatus.Processed, result.Status);
        var manifest = ManifestJson.Deserialize((await fixture.Store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(result.AssetId!)))!.Content.ToString());
        Assert.Contains(manifest.Derivatives, d => d.Name == "proxy-1080p");
        Assert.Null(await fixture.Store.ReadAsync(BlobLayout.OriginalsContainer, result.AssetId!));
    }

    [Fact]
    public async Task A_file_with_a_video_extension_and_no_video_is_left_out_with_no_manifest()
    {
        var tools = FfmpegTools.Find(null) ?? throw new InvalidOperationException("Integration tests need ffmpeg and ffprobe on the PATH.");
        await CreateContainersAsync();
        var bytes = "this is not a video, only text with an extension"u8.ToArray();
        var name = "novideo-" + Guid.NewGuid().ToString("N");
        await PutAsync(name, bytes, new IncomingFile("gdrive", "Família", "Clips/fake.mp4", IncomingMode.Custody));

        var result = await Processor(new VideoProcessor(tools)).ProcessAsync(name, CancellationToken.None);

        Assert.True(result.Status is CloudFileStatus.LeftOut or CloudFileStatus.Problem, result.Reason);
        Assert.Null(await fixture.Store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(MediaFile.Sha256Hex(bytes))));
    }

    private async Task CreateContainersAsync()
    {
        foreach (var container in new[] { BlobLayout.IncomingContainer, BlobLayout.OriginalsContainer, BlobLayout.DerivativesContainer, BlobLayout.ManifestsContainer })
        {
            await fixture.Blobs.GetBlobContainerClient(container).CreateIfNotExistsAsync();
        }
    }

    private Task<BlobWriteResult> PutAsync(string name, byte[] bytes, IncomingFile file) =>
        fixture.Store.WriteAsync(BlobLayout.IncomingContainer, name, BinaryData.FromBytes(bytes), "application/octet-stream", BlobWriteCondition.Always, file.ToMetadata());

    private CloudFileProcessor Processor(IVideoProcessor? video) =>
        new(fixture.Store, new ManifestStore(new ManifestValidator(), fixture.Store), video, new FakeEmbedder(), faces: null, TimeProvider.System, NullLogger.Instance);

    private static void Run(string tool, params string[] arguments)
    {
        var info = new ProcessStartInfo(tool) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
    }
}
