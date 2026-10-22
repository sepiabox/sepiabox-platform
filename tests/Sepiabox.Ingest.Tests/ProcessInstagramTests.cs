using System.Text;
using System.Text.Json.Nodes;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// <c>process --export instagram</c> (S-048, D-111) over synthetic downloads generated here: tiny photos and hand-written
/// JSON, no real person's data. The layout and fields are believed, not confirmed against a real download (D-111).
/// </summary>
public sealed class ProcessInstagramTests : IDisposable
{
    private const string Posts = "your_instagram_activity/content/posts_1.json";
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void Dates_a_post_from_its_creation_timestamp_in_the_time_zone_given_with_its_caption_and_album()
    {
        _drive.Write("media/posts/201907/a.jpg", Photo("a"));
        Json(Posts, """[{"media": [{"uri": "media/posts/201907/a.jpg", "creation_timestamp": 1561975200, "title": "Praia"}]}]""");

        var result = Process();

        Assert.Empty(result.Problems);
        var json = ReadManifestJson();
        Assert.Equal("2019-07-01T11:00:00", (string?)json["capturedAt"]);
        Assert.Equal("export", (string?)json["capturedAtSource"]);
        Assert.Equal("Praia", (string?)json["caption"]);
        Assert.Equal(["Instagram"], json["albums"]!.AsArray().Select(a => (string?)a));
        var source = json["sources"]![0]!.AsObject();
        Assert.Equal("instagram", (string?)source["type"]);
        Assert.False(source.ContainsKey("url"));
        Assert.True(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void Uses_the_exif_date_and_place_and_repairs_the_caption()
    {
        _drive.Write("media/posts/a.jpg", Photo("a"));
        Json(Posts, """
            [{"media": [{"uri": "media/posts/a.jpg", "creation_timestamp": 1561975200, "title": "aniversÃ¡rio",
              "media_metadata": {"photo_metadata": {"exif_data": [{"latitude": 38.7223, "longitude": -9.1393, "date_time_original": "2019:06:30 18:00:00"}]}}}]}]
            """);

        Process();

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2019, 6, 30, 18, 0, 0), CaptureDateSource.Export), (manifest.CapturedAt, manifest.CapturedAtSource));
        Assert.Equal(38.7223, manifest.Location!.Latitude, 4);
        Assert.Equal("aniversário", manifest.Caption);
    }

    [Fact]
    public void Gives_a_carousel_one_caption_and_each_file_its_own_time_and_leaves_unlisted_media_out()
    {
        _drive.Write("media/posts/a.jpg", Photo("a"));
        _drive.Write("media/posts/b.jpg", Photo("b"));
        _drive.Write("media/profile/me.jpg", Photo("me"));
        Json(Posts, """[{"title": "Weekend", "media": [{"uri": "media/posts/a.jpg", "creation_timestamp": 1561975200}, {"uri": "media/posts/b.jpg", "creation_timestamp": 1561975800}]}]""");

        var result = Process();

        Assert.Equal(2, result.Processed);
        var manifests = ReadManifests().OrderBy(m => m.CapturedAt).ToList();
        Assert.All(manifests, m => Assert.Equal("Weekend", m.Caption));
        Assert.Equal([new DateTime(2019, 7, 1, 11, 0, 0), new DateTime(2019, 7, 1, 11, 10, 0)], manifests.Select(m => m.CapturedAt!.Value));
        Assert.Contains("1 photos and videos in the download are in no post list", result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void An_index_only_run_says_the_original_is_not_stored_and_has_no_source_link()
    {
        _drive.Write("media/posts/a.jpg", Photo("a"));
        Json(Posts, """[{"media": [{"uri": "media/posts/a.jpg", "creation_timestamp": 1561975200}]}]""");

        var result = ProcessCommand.Run(
            new ProcessOptions(
                _drive.Path, _work.Path, "Instagram", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None, IndexOnly: true,
                ExportKind: "instagram", TimeZone: ProcessOptions.FindTimeZone("Europe/Lisbon")));

        Assert.Empty(result.Problems);
        var json = ReadManifestJson();
        Assert.False((bool?)json["originalStored"]);
        Assert.False(json["sources"]![0]!.AsObject().ContainsKey("url"));
        Assert.True(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void Does_not_touch_the_download()
    {
        _drive.Write("media/posts/a.jpg", Photo("a"));
        Json(Posts, """[{"media": [{"uri": "media/posts/a.jpg", "title": "x"}]}]""");
        var old = new DateTime(2016, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (var file in Directory.GetFiles(_drive.Path, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, old);
        }

        var before = Snapshot();

        Process();

        Assert.Equal(before, Snapshot());
    }

    private static byte[] Photo(string text)
    {
        using var image = new ImageMagick.MagickImage(new ImageMagick.MagickColor(ImageMagick.MagickColors.Gray), 32, 32);
        image.Settings.SetDefine("comment", text);
        image.Comment = text;
        return image.ToByteArray(ImageMagick.MagickFormat.Jpeg);
    }

    private void Json(string relative, string json) => _drive.Write(relative, Encoding.UTF8.GetBytes(json));

    private ProcessResult Process() =>
        ProcessCommand.Run(new ProcessOptions(
            _drive.Path, _work.Path, "Instagram", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None,
            ExportKind: "instagram", TimeZone: ProcessOptions.FindTimeZone("Europe/Lisbon")));

    private List<(string Path, long Size, DateTime Modified)> Snapshot() =>
        [.. Directory.GetFiles(_drive.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => (f[_drive.Path.Length..], new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))];

    private List<Manifest> ReadManifests() =>
        [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Select(f => ManifestJson.Deserialize(File.ReadAllText(f)))];

    private Manifest ReadManifest() => Assert.Single(ReadManifests());

    private JsonObject ReadManifestJson() =>
        JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json"))))!.AsObject();
}
