using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Storage;
using Sepiabox.Worker.Cloud;

namespace Sepiabox.Worker.Tests.Cloud;

/// <summary>
/// <see cref="CloudFileProcessor"/> and <c>ProcessCommand</c> share their describe, derivative and step code (S-065, D-118); this
/// test stays as the guard against what each does around it drifting: the same photos through <c>process --index-only</c> and
/// through the worker in reference mode must give the same manifests, source apart.
/// </summary>
public sealed class ProcessParityTests
{
    [Fact]
    public async Task The_worker_in_reference_mode_gives_the_manifests_of_process_index_only_except_for_the_source()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        using var noModels = new TempFolder();
        var photos = new Dictionary<string, byte[]>
        {
            ["Fotos/porto.jpg"] = TestPhotos.Porto("skyblue"),
            ["Fotos/2019/praia.jpg"] = TestPhotos.Taken("2019:07:14 10:30:00", "khaki"),
            ["Verão 2018/IMG_20180802_101500.jpg"] = Plain("salmon"),
        };
        foreach (var (path, bytes) in photos)
        {
            drive.Write(path, bytes);
        }

        ProcessCommand.Run(new ProcessOptions(drive.Path, work.Path, "HD", FfmpegFolder: TestFfmpeg.None, ModelFolder: noModels.Path, IndexOnly: true));
        var local = Directory.GetFiles(Path.Combine(work.Path, "manifests"), "*.json")
            .Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))
            .ToDictionary(m => m.Id);

        var store = new InMemoryBlobStore();
        foreach (var container in new[] { "incoming", "originals", "derivatives", "manifests" })
        {
            store.CreateContainer(container);
        }

        var processor = new CloudFileProcessor(store, new ManifestStore(new ManifestValidator(), store), null, null, null, TimeProvider.System, NullLogger.Instance);
        var n = 0;
        foreach (var (path, bytes) in photos)
        {
            var file = new IncomingFile("drive", "HD", path, IncomingMode.Reference, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
            await store.WriteAsync("incoming", "b" + n++, BinaryData.FromBytes(bytes), "application/octet-stream", BlobWriteCondition.Always, file.ToMetadata());
            Assert.Equal(CloudFileStatus.Processed, (await processor.ProcessAsync("b" + (n - 1), CancellationToken.None)).Status);
        }

        Assert.Equal(photos.Count, local.Count);
        foreach (var (id, expected) in local)
        {
            var actual = ManifestJson.Deserialize((await store.ReadAsync("manifests", BlobLayout.ManifestBlobName(id)))!.Content.ToString());
            Assert.Equal(Comparable(expected), Comparable(actual));
            Assert.False(actual.IsOriginalStored);
        }
    }

    // The worker's manifest has been through the merge, which keeps the derivatives in name order; the folder's has not.
    private static string Comparable(Manifest manifest) =>
        ManifestJson.Serialize(manifest with { Sources = [], Derivatives = [.. manifest.Derivatives.OrderBy(d => d.Name, StringComparer.Ordinal)] });

    private static byte[] Plain(string color)
    {
        using var image = new MagickImage(new MagickColor(color), 64, 48);
        return image.ToByteArray(MagickFormat.Jpeg);
    }
}
