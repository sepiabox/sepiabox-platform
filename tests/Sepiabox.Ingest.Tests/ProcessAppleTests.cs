using System.Text;
using System.Text.Json.Nodes;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// <c>process --export apple-photos</c> (S-047, D-109) over synthetic Apple exports generated here: tiny photos with CSV and
/// XMP written by hand, no real person's data. The layouts are believed, not confirmed against a real export (D-109).
/// </summary>
public sealed class ProcessAppleTests : IDisposable
{
    private const string Header = "imgName,fileChecksum,favorite,hidden,deleted,originalCreationDate,viewCount,importDate";
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void A_photos_app_export_gives_the_caption_and_the_date_of_the_xmp_sidecar()
    {
        _drive.Write("IMG_0001.jpg", Photo("a"));
        Text("IMG_0001.jpg.xmp", Xmp("""
            <dc:description><rdf:Alt><rdf:li xml:lang="x-default">Beach day</rdf:li></rdf:Alt></dc:description>
            <photoshop:DateCreated>2018-05-23T16:16:45</photoshop:DateCreated>
            <exif:GPSLatitude>38,43.338N</exif:GPSLatitude><exif:GPSLongitude>9,8.358W</exif:GPSLongitude>
            """));

        var result = Process();

        Assert.Empty(result.Problems);
        var manifest = ReadManifest();
        Assert.Equal("Beach day", manifest.Caption);
        Assert.Equal((new DateTime(2018, 5, 23, 16, 16, 45), CaptureDateSource.Export), (manifest.CapturedAt, manifest.CapturedAtSource));
        Assert.Equal(38.7223, manifest.Location!.Latitude, 4);
        var source = Assert.Single(manifest.Sources);
        Assert.Equal(("apple-photos", null), (source.Type, source.Url));
        Assert.True(new ManifestValidator().TryValidate(ReadManifestJson().ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void A_privacy_copy_dates_an_undated_file_from_its_details_row_in_the_time_zone_given()
    {
        _drive.Write("Photos/IMG_0001.jpg", Photo("a"));
        Text("Photo Details.csv", $"{Header}\nIMG_0001.jpg,x,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",1,\n");

        var result = Process();

        Assert.Empty(result.Problems);
        var json = ReadManifestJson();
        Assert.Equal("2018-05-23T17:16:00", (string?)json["capturedAt"]);
        Assert.Equal("export", (string?)json["capturedAtSource"]);
    }

    [Fact]
    public void Keeps_the_exif_date_of_a_file_over_the_export()
    {
        _drive.Write("a.jpg", TestPhotos.Porto());
        Text("Photo Details.csv", $"{Header}\na.jpg,x,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",1,\n");

        Process();

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2019, 7, 14, 16, 30, 5), CaptureDateSource.Metadata), (manifest.CapturedAt, manifest.CapturedAtSource));
    }

    [Fact]
    public void An_album_list_puts_both_files_in_the_album()
    {
        _drive.Write("Photos/IMG_0001.jpg", Photo("a"));
        _drive.Write("Photos/IMG_0002.jpg", Photo("b"));
        _drive.Write("Photos/IMG_0003.jpg", Photo("c"));
        Text("Albums/Holidays.csv", "Images\nIMG_0001.jpg\nIMG_0002.jpg\n");

        Process();

        var byName = ReadManifests().ToDictionary(m => Path.GetFileName(m.Sources.Single().Path));
        Assert.Equal(["Holidays"], byName["IMG_0001.jpg"].Albums);
        Assert.Equal(["Holidays"], byName["IMG_0002.jpg"].Albums);
        Assert.Null(byName["IMG_0003.jpg"].Albums);
    }

    [Fact]
    public void A_file_marked_deleted_is_left_out_and_the_warning_says_so()
    {
        _drive.Write("Photos/IMG_0001.jpg", Photo("kept"));
        _drive.Write("Photos/IMG_0002.jpg", Photo("deleted"));
        Text("Photo Details.csv", $"{Header}\nIMG_0001.jpg,x,N,N,N,,0,\nIMG_0002.jpg,y,N,N,Y,,0,\n");

        var result = Process();

        Assert.Equal(1, result.Processed);
        Assert.Equal("Photos/IMG_0001.jpg", Assert.Single(ReadManifest().Sources).Path);
        Assert.Contains("1 photos and videos are marked deleted in Photo Details and were not imported.", result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_sidecars_make_no_manifest_and_no_problem()
    {
        _drive.Write("Photos/IMG_0001.jpg", Photo("a"));
        _drive.Write("Photos/IMG_O0001.AAE", [1, 2, 3]);
        _drive.Write("Photos/IMG_0001.jpg.xmp", [1]);

        var result = Process();

        Assert.Empty(result.Problems);
        Assert.Equal(1, result.Processed);
        Assert.Single(ReadManifests());
    }

    [Fact]
    public void An_index_only_run_says_the_original_is_not_stored_and_has_no_source_link()
    {
        _drive.Write("Photos/IMG_0001.jpg", Photo("a"));
        Text("Photo Details.csv", $"{Header}\nIMG_0001.jpg,x,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",1,\n");

        var result = ProcessCommand.Run(
            new ProcessOptions(
                _drive.Path, _work.Path, "Apple", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None, IndexOnly: true,
                ExportKind: "apple-photos", TimeZone: ProcessOptions.FindTimeZone("Europe/Lisbon")));

        Assert.Empty(result.Problems);
        var json = ReadManifestJson();
        Assert.False((bool?)json["originalStored"]);
        var source = json["sources"]![0]!.AsObject();
        Assert.False(source.ContainsKey("url"));
        Assert.Equal("apple-photos", (string?)source["type"]);
        Assert.Equal("export", (string?)json["capturedAtSource"]);
        Assert.True(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void Does_not_touch_the_export()
    {
        _drive.Write("Photos/IMG_0001.jpg", Photo("a"));
        Text("Photo Details.csv", $"{Header}\nIMG_0001.jpg,x,N,N,Y,,0,\n");
        Text("Albums/Holidays.csv", "Images\nIMG_0001.jpg\n");
        var old = new DateTime(2016, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (var file in Directory.GetFiles(_drive.Path, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, old);
        }

        var before = Snapshot();

        Process();

        Assert.Equal(before, Snapshot());
        Assert.False(Directory.Exists(Path.Combine(_drive.Path, "manifests")));
    }

    private static string Xmp(string properties) =>
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:photoshop=\"http://ns.adobe.com/photoshop/1.0/\" xmlns:exif=\"http://ns.adobe.com/exif/1.0/\">"
        + properties + "</rdf:Description></rdf:RDF></x:xmpmeta>";

    private static byte[] Photo(string text)
    {
        using var image = new ImageMagick.MagickImage(new ImageMagick.MagickColor(ImageMagick.MagickColors.Gray), 32, 32);
        image.Settings.SetDefine("comment", text);
        image.Comment = text;
        return image.ToByteArray(ImageMagick.MagickFormat.Jpeg);
    }

    private void Text(string relative, string text) => _drive.Write(relative, Encoding.UTF8.GetBytes(text));

    private ProcessResult Process() =>
        ProcessCommand.Run(new ProcessOptions(
            _drive.Path, _work.Path, "Apple", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None,
            ExportKind: "apple-photos", TimeZone: ProcessOptions.FindTimeZone("Europe/Lisbon")));

    private List<(string Path, long Size, DateTime Modified)> Snapshot() =>
        [.. Directory.GetFiles(_drive.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => (f[_drive.Path.Length..], new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))];

    private List<Manifest> ReadManifests() =>
        [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Select(f => ManifestJson.Deserialize(File.ReadAllText(f)))];

    private Manifest ReadManifest() => Assert.Single(ReadManifests());

    private JsonObject ReadManifestJson() =>
        JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json"))))!.AsObject();
}
