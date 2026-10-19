using System.Text.Json.Nodes;
using Json.Schema;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Tests.Shared;

namespace Sepiabox.Ingest.Tests;

public class ProcessCommandTests
{
    [Fact]
    public void Describes_a_photo_from_its_own_metadata()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("2019/praia.jpg", TestPhotos.Porto());

        var result = Process(drive, work);

        var manifest = Assert.Single(ReadManifests(work));
        Assert.Equal(1, result.Processed);
        Assert.Matches("^[0-9a-f]{64}$", manifest.Id);
        Assert.Equal(MediaKind.Photo, manifest.Kind);
        Assert.Equal("image/jpeg", manifest.ContentType);
        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 5), manifest.CapturedAt);
        Assert.Equal(new GeoPoint(41.18, -8.69), manifest.Location);
        Assert.Equal(new CameraInfo("Apple", "iPhone 8"), manifest.Camera);
        Assert.Equal((64, 48), (manifest.Width, manifest.Height));

        var source = Assert.Single(manifest.Sources);
        Assert.Equal(("local", "HD da Ana", "2019/praia.jpg"), (source.Type, source.Name, source.Path));
    }

    [Fact]
    public void Names_the_source_after_the_folder_by_default()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("photo.jpg", TestPhotos.Porto());

        ProcessCommand.Run(new ProcessOptions(drive.Path, work.Path, SourceName: null));

        Assert.Equal(Path.GetFileName(drive.Path), Assert.Single(Assert.Single(ReadManifests(work)).Sources).Name);
    }

    [Fact]
    public void Writes_one_manifest_with_every_source_for_identical_files()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        var photo = TestPhotos.Porto();
        drive.Write("2019/praia.jpg", photo);
        drive.Write("backup/praia-copy.jpg", photo);

        Process(drive, work);

        var manifest = Assert.Single(ReadManifests(work));
        Assert.Equal(["2019/praia.jpg", "backup/praia-copy.jpg"], manifest.Sources.Select(source => source.Path));
    }

    [Fact]
    public void Skips_every_file_when_nothing_changed_since_the_last_run()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("a.jpg", TestPhotos.Porto("red"));
        drive.Write("b.jpg", TestPhotos.Porto("green"));
        drive.Write("c.mov", RandomBytes(4_000));
        Process(drive, work);

        var second = Process(drive, work);

        Assert.Equal(0, second.Processed);
        Assert.Equal(3, second.Skipped);
        Assert.Equal(3, ReadManifests(work).Count);
    }

    [Fact]
    public void Reprocesses_a_file_whose_content_changed_and_drops_the_old_description()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        var path = drive.Write("edited.jpg", TestPhotos.Porto("red"));
        Process(drive, work);
        var before = Assert.Single(ReadManifests(work));

        File.WriteAllBytes(path, TestPhotos.Porto("blue"));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        var result = Process(drive, work);

        var after = Assert.Single(ReadManifests(work));
        Assert.Equal(1, result.Processed);
        Assert.NotEqual(before.Id, after.Id);
    }

    [Fact]
    public void Writes_manifests_that_match_the_v1_schema()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("porto.jpg", TestPhotos.Porto());
        drive.Write("plain.png", TestPhotos.Plain());
        drive.Write("clip.mp4", RandomBytes(3_000));
        drive.Write("notes.txt", RandomBytes(10));
        Process(drive, work);

        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "manifest.v1.json")));
        var files = Directory.GetFiles(Path.Combine(work.Path, "manifests"), "*.json");

        Assert.Equal(3, files.Length);
        foreach (var file in files)
        {
            var evaluation = schema.Evaluate(JsonNode.Parse(File.ReadAllText(file)), new EvaluationOptions { OutputFormat = OutputFormat.List });
            Assert.True(evaluation.IsValid, $"{Path.GetFileName(file)} does not match the schema.");
        }
    }

    [Fact]
    public void Processes_read_only_files_without_errors()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        var path = drive.Write("locked.jpg", TestPhotos.Porto());
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var result = Process(drive, work);

            Assert.Equal(1, result.Processed);
            Assert.Empty(result.Problems);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Ignores_its_own_work_folder_inside_the_source()
    {
        using var drive = new TempFolder();
        drive.Write("photo.jpg", TestPhotos.Porto());
        var work = Path.Combine(drive.Path, ".sepiabox");
        drive.Write(".sepiabox/manifests/leftover.jpg", TestPhotos.Plain());

        var result = ProcessCommand.Run(new ProcessOptions(drive.Path, work, "HD"));

        Assert.Equal(1, result.Processed);
    }

    private static ProcessResult Process(TempFolder drive, TempFolder work) =>
        ProcessCommand.Run(new ProcessOptions(drive.Path, work.Path, "HD da Ana"));

    private static List<Manifest> ReadManifests(TempFolder work) =>
        [.. Directory.GetFiles(Path.Combine(work.Path, "manifests"), "*.json").Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }
}
