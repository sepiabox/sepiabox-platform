using System.Text;
using System.Text.Json.Nodes;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// Capture dates that <c>process</c> records (D-046). Videos are fake bytes with no metadata, and FFmpeg is pointed
/// at a folder without it, so they stay pending but their manifests are written.
/// </summary>
public sealed class ProcessDateTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void Dates_a_camcorder_clip_from_its_folder_and_file_name_when_it_has_no_metadata_date()
    {
        _drive.Write("Life/2011-09-25/MNU_110425_d8b2.MTS", FakeVideo("clip"));

        var result = Process();

        Assert.Empty(result.Problems);
        var json = ReadManifestJson();
        Assert.Equal("2011-09-25T11:04:25", (string?)json["capturedAt"]);
        Assert.Equal("fileName", (string?)json["capturedAtSource"]);
        Assert.True(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void Dates_a_clip_at_midnight_of_its_folder_when_its_name_has_no_time()
    {
        _drive.Write("2008-12-24/clip.mod", FakeVideo("mod"));

        Process();

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2008, 12, 24), CaptureDateSource.Folder), (manifest.CapturedAt, manifest.CapturedAtSource));
        Assert.Equal((MediaKind.Video, "video/mpeg"), (manifest.Kind, manifest.ContentType));
    }

    [Fact]
    public void Dates_a_phone_video_from_its_file_name_when_it_has_no_metadata_date()
    {
        _drive.Write("Telemovel/VID_20120304_101112.mp4", FakeVideo("phone"));

        Process();

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName), (manifest.CapturedAt, manifest.CapturedAtSource));
    }

    [Fact]
    public void Keeps_the_exif_date_when_the_folder_names_another_day()
    {
        _drive.Write("2005-07-14/praia.jpg", TestPhotos.Porto());

        Process();

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2019, 7, 14, 16, 30, 5), CaptureDateSource.Metadata), (manifest.CapturedAt, manifest.CapturedAtSource));
    }

    [Fact]
    public void Dates_a_photo_from_its_folder_when_its_exif_date_is_before_1990()
    {
        _drive.Write("2005-07-14/reset.jpg", TestPhotos.Taken("1980:01:01 00:00:00"));

        Process();

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2005, 7, 14), CaptureDateSource.Folder), (manifest.CapturedAt, manifest.CapturedAtSource));
    }

    [Fact]
    public void Dates_a_photo_from_its_folder_when_its_exif_date_is_after_now()
    {
        _drive.Write("2005-07-14/future.jpg", TestPhotos.Taken("2026:12:25 10:00:00"));

        ProcessCommand.Run(Options(), embedder: null, "no model in this test", clock: new FixedClock(new DateTime(2026, 10, 20)));

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2005, 7, 14), CaptureDateSource.Folder), (manifest.CapturedAt, manifest.CapturedAtSource));
    }

    [Fact]
    public void Records_no_date_and_no_source_when_the_folder_is_not_a_real_day()
    {
        _drive.Write("2011-02-30/clip.mod", FakeVideo("feb"));

        Process();

        var json = ReadManifestJson();
        Assert.Null(json["capturedAt"]);
        Assert.False(json.ContainsKey("capturedAtSource"));
    }

    [Fact]
    public void Keeps_the_folder_date_when_a_copy_of_the_same_clip_is_in_an_undated_folder()
    {
        var clip = FakeVideo("copied");
        _drive.Write("2008-12-24/clip.mod", clip);
        _drive.Write("backup/clip.mod", clip);
        _drive.Write("zz-backup/clip.mod", clip);

        Process();

        var manifest = ReadManifest();
        Assert.Equal(3, manifest.Sources.Count);
        Assert.Equal((new DateTime(2008, 12, 24), CaptureDateSource.Folder), (manifest.CapturedAt, manifest.CapturedAtSource));
    }

    [Fact]
    public void Keeps_the_exif_date_when_a_copy_of_the_same_photo_is_in_a_dated_folder()
    {
        var photo = TestPhotos.Porto();
        _drive.Write("2005-07-14/praia.jpg", photo);
        _drive.Write("Fotos/praia.jpg", photo);
        _drive.Write("Fotos/IMG_20120304_101112.jpg", photo);

        Process();

        var manifest = ReadManifest();
        Assert.Equal(3, manifest.Sources.Count);
        Assert.Equal((new DateTime(2019, 7, 14, 16, 30, 5), CaptureDateSource.Metadata), (manifest.CapturedAt, manifest.CapturedAtSource));
    }

    [Fact]
    public void Leaves_the_source_files_unchanged_when_dating_them_from_their_path()
    {
        var path = _drive.Write("2011-09-25/MNU_110425_d8b2.MTS", FakeVideo("read-only"));
        var written = new DateTime(2011, 9, 26, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);
        var bytes = File.ReadAllBytes(path);

        Process();

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        Assert.Equal(["MNU_110425_d8b2.MTS"], Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    private ProcessOptions Options() =>
        new(_drive.Path, _work.Path, "HD", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None);

    private ProcessResult Process() => ProcessCommand.Run(Options());

    private Manifest ReadManifest() => ManifestJson.Deserialize(ReadManifestJson().ToJsonString());

    private JsonObject ReadManifestJson() =>
        JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json"))))!.AsObject();

    // Not a real video: no metadata, so the date can only come from the path. The text makes the hash unique.
    private static byte[] FakeVideo(string text) => Encoding.UTF8.GetBytes($"not really a video: {text}");
}
