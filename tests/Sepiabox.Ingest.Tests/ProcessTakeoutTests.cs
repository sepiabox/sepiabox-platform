using System.Text;
using System.Text.Json.Nodes;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// <c>process --export google-takeout</c> (S-046, D-106) over synthetic Takeout folders generated here: tiny photos and
/// hand-written JSON, no real person's data. The first test is the story's own example.
/// </summary>
public sealed class ProcessTakeoutTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void Dates_a_photo_with_no_exif_date_from_its_sidecar_in_the_time_zone_given()
    {
        _drive.Write("Takeout/Google Photos/Photos from 2019/IMG_0001.jpg", Photo("a"));
        Json("Takeout/Google Photos/Photos from 2019/IMG_0001.jpg.json", """{"title": "IMG_0001.jpg", "photoTakenTime": {"timestamp": "1561975200"}}""");

        var result = Process();

        Assert.Empty(result.Problems);
        var json = ReadManifestJson();
        Assert.Equal("2019-07-01T11:00:00", (string?)json["capturedAt"]);
        Assert.Equal("export", (string?)json["capturedAtSource"]);
        Assert.True(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void Keeps_the_exif_date_and_the_files_own_gps_over_the_sidecars()
    {
        _drive.Write("a.jpg", TestPhotos.Porto());
        Json("a.jpg.json", """{"photoTakenTime": {"timestamp": "1561975200"}, "geoData": {"latitude": 38.7223, "longitude": -9.1393}}""");

        Process();

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2019, 7, 14, 16, 30, 5), CaptureDateSource.Metadata), (manifest.CapturedAt, manifest.CapturedAtSource));
        Assert.Equal(41.18, manifest.Location!.Latitude, 2);
    }

    [Fact]
    public void Takes_the_location_from_geo_data_when_the_file_has_none_and_nothing_when_it_is_zero_zero()
    {
        _drive.Write("here.jpg", Photo("here"));
        _drive.Write("nowhere.jpg", Photo("nowhere"));
        Json("here.jpg.json", """{"geoData": {"latitude": 38.7223, "longitude": -9.1393}}""");
        Json("nowhere.jpg.json", """{"geoData": {"latitude": 0.0, "longitude": 0.0}, "geoDataExif": {"latitude": 0.0, "longitude": 0.0}}""");

        Process();

        var bySource = ReadManifests().ToDictionary(m => m.Sources.Single().Path);
        Assert.Equal(new GeoPoint(38.7223, -9.1393), bySource["here.jpg"].Location);
        Assert.Null(bySource["nowhere.jpg"].Location);
    }

    [Fact]
    public void Writes_the_description_as_the_caption()
    {
        _drive.Write("a.jpg", Photo("a"));
        Json("a.jpg.json", """{"title": "a.jpg", "description": "Grandma's birthday"}""");

        Process();

        Assert.Equal("Grandma's birthday", ReadManifest().Caption);
    }

    [Fact]
    public void Collects_the_albums_of_the_same_file_from_every_folder_it_is_in_and_leaves_the_year_folder_out()
    {
        var bytes = Photo("shared");
        _drive.Write("Photos from 2019/IMG_0001.jpg", bytes);
        _drive.Write("Summer in Portugal/IMG_0001.jpg", bytes);
        _drive.Write("Fim de semana/IMG_0001.jpg", bytes);
        Json("Photos from 2019/IMG_0001.jpg.json", """{"photoTakenTime": {"timestamp": "1561975200"}}""");
        Json("Summer in Portugal/IMG_0001.jpg.json", """{"photoTakenTime": {"timestamp": "1561975200"}}""");
        Json("Fim de semana/IMG_0001.jpg.json", """{"photoTakenTime": {"timestamp": "1561975200"}}""");
        Json("Summer in Portugal/metadata.json", """{"title": "Summer in Portugal"}""");

        Process();

        var manifest = ReadManifest();
        Assert.Equal(["Fim de semana", "Summer in Portugal"], manifest.Albums);
        Assert.Equal(3, manifest.Sources.Count);
        Assert.All(manifest.Sources, s => Assert.Equal("google-takeout", s.Type));
    }

    [Fact]
    public void Matches_each_naming_quirk_through_process()
    {
        _drive.Write("Trip/IMG_0001.jpg", Photo("plain"));
        _drive.Write("Trip/IMG_0002.jpg", Photo("supplemental"));
        _drive.Write("Trip/IMG_0003-edited.jpg", Photo("edited"));
        _drive.Write("Trip/IMG_0004.jpg", Photo("first copy"));
        _drive.Write("Trip/IMG_0004(1).jpg", Photo("second copy"));
        var longName = "A_PXL_20190701_101010123.MP.COVER.ORIGINAL.photo.jpg";
        _drive.Write("Trip/" + longName, Photo("long"));
        Json("Trip/IMG_0001.jpg.json", """{"description": "plain"}""");
        Json("Trip/IMG_0002.jpg.supplemental-metadata.json", """{"description": "supplemental"}""");
        Json("Trip/IMG_0003.jpg.json", """{"description": "edited"}""");
        Json("Trip/IMG_0004.jpg.json", """{"description": "first copy"}""");
        Json("Trip/IMG_0004.jpg(1).json", """{"description": "second copy"}""");
        Json("Trip/" + (longName + TakeoutSidecarsSupplemental)[..46] + ".json", """{"description": "long"}""");

        var result = Process();

        var captions = ReadManifests().ToDictionary(m => m.Sources.Single().Path, m => m.Caption);
        Assert.Equal("plain", captions["Trip/IMG_0001.jpg"]);
        Assert.Equal("supplemental", captions["Trip/IMG_0002.jpg"]);
        Assert.Equal("edited", captions["Trip/IMG_0003-edited.jpg"]);
        Assert.Equal("first copy", captions["Trip/IMG_0004.jpg"]);
        Assert.Equal("second copy", captions["Trip/IMG_0004(1).jpg"]);
        Assert.Equal("long", captions["Trip/" + longName]);
        Assert.DoesNotContain("no JSON sidecar", result.Warning ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Processes_a_file_without_a_sidecar_with_its_own_metadata_and_warns_once()
    {
        _drive.Write("a.jpg", TestPhotos.Porto());
        _drive.Write("b.jpg", Photo("b"));
        _drive.Write("c.jpg", Photo("c"));

        var result = Process();

        Assert.Empty(result.Problems);
        Assert.Contains("3 photos and videos in the export have no JSON sidecar and were read from their own metadata only.", result.Warning, StringComparison.Ordinal);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(result.Warning!, "no JSON sidecar"));
        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 5), ReadManifests().Single(m => m.Sources.Single().Path == "a.jpg").CapturedAt);
    }

    [Theory]
    [InlineData("https://photos.google.com/photo/AF1Qip-example", "https://photos.google.com/photo/AF1Qip-example")]
    [InlineData("http://photos.google.com/photo/x", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(null, null)]
    public void Puts_the_sidecars_https_url_on_the_source_and_none_otherwise(string? url, string? expected)
    {
        _drive.Write("a.jpg", Photo("a"));
        Json("a.jpg.json", url is null ? "{}" : new JsonObject { ["url"] = url }.ToJsonString());

        Process();

        var source = Assert.Single(ReadManifest().Sources);
        Assert.Equal(("google-takeout", expected), (source.Type, source.Url));
        Assert.True(new ManifestValidator().TryValidate(ReadManifestJson().ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void An_index_only_run_keeps_the_url_and_says_the_original_is_not_stored()
    {
        _drive.Write("Trip/a.jpg", Photo("a"));
        Json("Trip/a.jpg.json", """{"url": "https://photos.google.com/photo/AF1Qip-example", "photoTakenTime": {"timestamp": "1561975200"}}""");

        var result = ProcessCommand.Run(
            new ProcessOptions(
                _drive.Path, _work.Path, "Takeout", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None, IndexOnly: true,
                ExportKind: "google-takeout", TimeZone: ProcessOptions.FindTimeZone("Europe/Lisbon")));

        Assert.Empty(result.Problems);
        var json = ReadManifestJson();
        Assert.False((bool?)json["originalStored"]);
        Assert.Equal("https://photos.google.com/photo/AF1Qip-example", (string?)json["sources"]![0]!["url"]);
        Assert.Equal("2019-07-01T11:00:00", (string?)json["capturedAt"]);
    }

    [Fact]
    public void Does_not_import_what_is_in_trash_and_keeps_archived_photos_without_an_album()
    {
        _drive.Write("Trash/gone.jpg", Photo("gone"));
        _drive.Write("Archive/kept.jpg", Photo("kept"));
        Json("Trash/gone.jpg.json", """{"description": "deleted"}""");
        Json("Archive/kept.jpg.json", """{"description": "kept", "archived": true}""");

        var result = Process();

        var manifest = ReadManifest();
        Assert.Equal("kept", manifest.Caption);
        Assert.Null(manifest.Albums);
        Assert.Contains("1 items in Trash were not imported.", result.Warning, StringComparison.Ordinal);
        Assert.Equal(1, result.Processed);
    }

    [Fact]
    public void Does_not_touch_the_export()
    {
        _drive.Write("Trip/a.jpg", Photo("a"));
        Json("Trip/a.jpg.json", """{"description": "x"}""");
        Json("Trip/metadata.json", """{"title": "Trip"}""");
        var old = new DateTime(2016, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (var file in Directory.GetFiles(_drive.Path, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, old);
        }

        var before = Snapshot();

        Process();

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Is_a_known_kind_of_export()
    {
        Assert.True(ProcessOptions.TryParse(["/in", "--out", "/out", "--export", "google-takeout", "--time-zone", "Europe/Lisbon"], out var options, out var error), error);
        Assert.Equal("google-takeout", options.ExportKind);
        Assert.False(ProcessOptions.TryParse(["/in", "--out", "/out", "--export", "takeout"], out _, out var unknown));
        Assert.Equal("Unknown export kind 'takeout'. Known kinds: apple-photos, google-takeout, instagram.", unknown);
    }

    private const string TakeoutSidecarsSupplemental = ".supplemental-metadata";

    // A different photo each time, with no date and no place of its own.
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
            _drive.Path, _work.Path, "Takeout", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None,
            ExportKind: "google-takeout", TimeZone: ProcessOptions.FindTimeZone("Europe/Lisbon")));

    private List<(string Path, long Size, DateTime Modified)> Snapshot() =>
        [.. Directory.GetFiles(_drive.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => (f[_drive.Path.Length..], new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))];

    private List<Manifest> ReadManifests() =>
        [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Select(f => ManifestJson.Deserialize(File.ReadAllText(f)))];

    private Manifest ReadManifest() => Assert.Single(ReadManifests());

    private JsonObject ReadManifestJson() =>
        JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json"))))!.AsObject();
}
