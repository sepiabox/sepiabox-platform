using System.Text.Json.Nodes;
using Json.Schema;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

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

        // These tests run without FFmpeg, so the video stays pending without being read again.
        Assert.Equal(0, second.Processed);
        Assert.Equal(2, second.Skipped);
        Assert.Equal(1, second.VideosPending);
        Assert.Equal(3, ReadManifests(work).Count);
    }

    [Fact]
    public void Describes_videos_and_leaves_them_pending_with_a_warning_when_ffmpeg_is_missing()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        var video = drive.Write("clip.mp4", RandomBytes(3_000));
        drive.Write("photo.jpg", TestPhotos.Porto());

        var result = Process(drive, work);

        Assert.Empty(result.Problems);
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.VideosPending);
        Assert.Contains("FFmpeg was not found", result.Warning, StringComparison.Ordinal);
        Assert.Empty(Assert.Single(ReadManifests(work), m => m.Kind == MediaKind.Video).Derivatives);
        using var state = new StateStore(Path.Combine(work.Path, "state.db"));
        Assert.Equal(FileStatus.Pending, state.Get(video)?.Status);
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

    [Fact]
    public void Writes_photo_thumbnails_and_lists_them_in_the_manifest()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("big.jpg", TestPhotos.Sized(4000, 3000));

        Process(drive, work);

        var manifest = Assert.Single(ReadManifests(work));
        Assert.Equal(
            [
                new ManifestDerivative("thumb-256", $"derivatives/{manifest.Id}/thumb-256.webp", 256, 192, "image/webp"),
                new ManifestDerivative("preview-1280", $"derivatives/{manifest.Id}/preview-1280.webp", 1280, 960, "image/webp"),
            ],
            manifest.Derivatives);
        Assert.All(manifest.Derivatives, d => Assert.True(File.Exists(Path.Combine(work.Path, d.Path))));
    }

    [Fact]
    public void Keeps_the_manifest_without_derivatives_and_reports_a_corrupted_photo()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("broken.jpg", RandomBytes(2_000));
        drive.Write("good.jpg", TestPhotos.Porto());

        var result = Process(drive, work);

        var problem = Assert.Single(result.Problems);
        Assert.EndsWith("broken.jpg", problem.Path, StringComparison.Ordinal);
        var broken = Assert.Single(ReadManifests(work), m => m.Sources[0].Path == "broken.jpg");
        Assert.Empty(broken.Derivatives);
        Assert.Equal(1, result.Processed);
    }

    [Fact]
    public void Does_not_regenerate_thumbnails_when_nothing_changed()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("photo.jpg", TestPhotos.Porto());
        Process(drive, work);
        var thumb = Path.Combine(work.Path, Assert.Single(ReadManifests(work)).Derivatives[0].Path);
        var firstWrite = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(thumb, firstWrite);

        var second = Process(drive, work);

        Assert.Equal(1, second.Skipped);
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(thumb));
    }

    [Fact]
    public void Deletes_the_thumbnails_of_a_description_that_no_longer_has_any_source()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        var path = drive.Write("edited.jpg", TestPhotos.Porto("red"));
        Process(drive, work);
        var oldFolder = Path.Combine(work.Path, "derivatives", Assert.Single(ReadManifests(work)).Id);

        File.WriteAllBytes(path, TestPhotos.Porto("blue"));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        Process(drive, work);

        Assert.False(Directory.Exists(oldFolder));
    }

    // Unit tests must behave the same with or without FFmpeg installed, so they point at a folder without it.
    [Fact]
    public void Without_the_clip_model_the_run_succeeds_and_says_how_to_get_it()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        using var noModels = new TempFolder();
        drive.Write("2019/praia.jpg", TestPhotos.Porto());

        var result = ProcessCommand.Run(new ProcessOptions(
            drive.Path, work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: noModels.Path));

        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.Embedded);
        Assert.Empty(result.Problems);
        Assert.Contains("sepiabox-ingest models download", result.Warning, StringComparison.Ordinal);
        Assert.Empty(Assert.Single(ReadManifests(work)).Embeddings);
    }

    private static ProcessResult Process(TempFolder drive, TempFolder work) =>
        ProcessCommand.Run(new ProcessOptions(drive.Path, work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None));

    private static List<Manifest> ReadManifests(TempFolder work) =>
        [.. Directory.GetFiles(Path.Combine(work.Path, "manifests"), "*.json").Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }
}
