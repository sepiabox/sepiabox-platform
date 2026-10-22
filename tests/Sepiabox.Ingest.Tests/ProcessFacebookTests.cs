using System.Text;
using System.Text.Json.Nodes;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// <c>process --export facebook</c> (S-049, D-112) over synthetic downloads generated here: tiny photos and hand-written
/// JSON, no real person's data. The layout and fields are believed, not confirmed against a real download (D-112).
/// </summary>
public sealed class ProcessFacebookTests : IDisposable
{
    private const string Album = "your_facebook_activity/posts/album/0.json";
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void Writes_the_taken_date_the_album_and_facebook_for_a_photo_of_an_album()
    {
        _drive.Write("your_facebook_activity/posts/media/a.jpg", Photo("a"));
        Json(Album, """
            {"name": "Viagem a Paris", "photos": [{"uri": "your_facebook_activity/posts/media/a.jpg", "creation_timestamp": 1561975800,
              "media_metadata": {"photo_metadata": {"exif_data": [{"taken_timestamp": 1561975200}]}}}]}
            """);

        var result = Process();

        Assert.Empty(result.Problems);
        var json = ReadManifestJson();
        Assert.Equal("2019-07-01T11:00:00", (string?)json["capturedAt"]);
        Assert.Equal("export", (string?)json["capturedAtSource"]);
        Assert.Equal(["Facebook", "Viagem a Paris"], json["albums"]!.AsArray().Select(a => (string?)a));
        var source = json["sources"]![0]!.AsObject();
        Assert.Equal("facebook", (string?)source["type"]);
        Assert.False(source.ContainsKey("url"));
        Assert.True(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void Takes_the_posts_text_as_the_caption_of_a_photo_in_an_album_and_a_post_and_the_creation_time_without_a_taken_one()
    {
        _drive.Write("media/a.jpg", Photo("a"));
        Json(Album, """{"name": "Mobile uploads", "photos": [{"uri": "media/a.jpg", "creation_timestamp": 1561975800}]}""");
        Json(
            "your_facebook_activity/posts/your_posts__check_ins__photos_and_videos_1.json",
            """[{"timestamp": 1561976000, "data": [{"post": "Dia na praia"}], "attachments": [{"data": [{"media": {"uri": "media/a.jpg", "creation_timestamp": 1561975800}}]}]}]""");

        Process();

        var manifest = ReadManifest();
        Assert.Equal("Dia na praia", manifest.Caption);
        Assert.Equal(new DateTime(2019, 7, 1, 11, 10, 0), manifest.CapturedAt);
        Assert.Equal(["Facebook", "Mobile uploads"], manifest.Albums);
    }

    [Fact]
    public void Gives_both_layouts_the_same_manifests_and_an_index_only_run_keeps_no_original_and_no_link()
    {
        _drive.Write("photos/a.jpg", Photo("a"));
        Json("posts/album/0.json", """{"name": "Paris", "photos": [{"uri": "photos/a.jpg", "creation_timestamp": 1561975200, "description": "x"}]}""");
        var older = ProcessInto(_work.Path, indexOnly: false);
        Directory.Delete(Path.Combine(_drive.Path, "posts"), true);
        Json(Album, """{"name": "Paris", "photos": [{"uri": "photos/a.jpg", "creation_timestamp": 1561975200, "description": "x"}]}""");
        using var otherWork = new TempFolder();
        var current = ProcessInto(otherWork.Path, indexOnly: true);

        Assert.Empty(older.Problems);
        Assert.Empty(current.Problems);
        var first = ReadManifests(_work.Path).Single();
        var second = JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(otherWork.Path, "manifests"), "*.json"))))!.AsObject();
        Assert.Equal(first.Id, (string?)second["id"]);
        Assert.Equal((first.CapturedAt, first.Caption), (DateTime.Parse((string)second["capturedAt"]!, System.Globalization.CultureInfo.InvariantCulture), (string?)second["caption"]));
        Assert.False((bool?)second["originalStored"]);
        Assert.False(second["sources"]![0]!.AsObject().ContainsKey("url"));
    }

    [Fact]
    public void Does_not_touch_the_download()
    {
        _drive.Write("media/a.jpg", Photo("a"));
        Json(Album, """{"name": "Paris", "photos": [{"uri": "media/a.jpg"}]}""");
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

    private ProcessResult Process() => ProcessInto(_work.Path, indexOnly: false);

    private ProcessResult ProcessInto(string work, bool indexOnly) =>
        ProcessCommand.Run(new ProcessOptions(
            _drive.Path, work, "Facebook", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None, IndexOnly: indexOnly,
            ExportKind: "facebook", TimeZone: ProcessOptions.FindTimeZone("Europe/Lisbon")));

    private List<(string Path, long Size, DateTime Modified)> Snapshot() =>
        [.. Directory.GetFiles(_drive.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => (f[_drive.Path.Length..], new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))];

    private static List<Manifest> ReadManifests(string work) =>
        [.. Directory.GetFiles(Path.Combine(work, "manifests"), "*.json").Select(f => ManifestJson.Deserialize(File.ReadAllText(f)))];

    private Manifest ReadManifest() => Assert.Single(ReadManifests(_work.Path));

    private JsonObject ReadManifestJson() =>
        JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json"))))!.AsObject();
}
