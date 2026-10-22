using System.Text.Json.Nodes;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Exports;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// <c>process --export</c> (S-045): a test reader, not any real export, tells <c>process</c> what the "export" says about
/// its files, and the mechanism puts it in the manifests the way D-104 says.
/// </summary>
public sealed class ProcessExportTests : IDisposable
{
    private static readonly TimeZoneInfo s_lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void Dates_an_undated_file_from_the_export_in_the_time_zone_given()
    {
        _drive.Write("a.png", TestPhotos.Plain());
        Describe("a.png", """{"capturedAtUtc": "2015-06-01T10:00:00Z"}""");

        var result = Process(s_lisbon);

        Assert.Empty(result.Problems);
        var json = ReadManifestJson();
        Assert.Equal("2015-06-01T11:00:00", (string?)json["capturedAt"]);
        Assert.Equal("export", (string?)json["capturedAtSource"]);
        Assert.True(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void Gives_the_source_the_kind_of_the_export_as_its_type_and_the_drive_label_as_its_name()
    {
        _drive.Write("Photos/a.png", TestPhotos.Plain());
        Describe("Photos/a.png", """{"caption": "x"}""");

        Process(s_lisbon);

        var source = Assert.Single(ReadManifest().Sources);
        Assert.Equal(("test", "HD", "Photos/a.png"), (source.Type, source.Name, source.Path));
    }

    [Fact]
    public void Keeps_the_local_source_type_without_export()
    {
        _drive.Write("a.png", TestPhotos.Plain());

        ProcessCommand.Run(new ProcessOptions(_drive.Path, _work.Path, "HD", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None));

        Assert.Equal("local", Assert.Single(ReadManifest().Sources).Type);
    }

    [Fact]
    public void Keeps_the_exif_date_when_the_export_gives_another()
    {
        _drive.Write("a.jpg", TestPhotos.Taken("2012:03:04 10:11:12"));
        Describe("a.jpg", """{"capturedAtUtc": "2015-06-01T10:00:00Z"}""");

        Process(s_lisbon);

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.Metadata), (manifest.CapturedAt, manifest.CapturedAtSource));
    }

    [Fact]
    public void Takes_the_export_date_over_the_folder_s_and_the_name_s()
    {
        _drive.Write("2005-07-14/a.png", TestPhotos.Plain());
        Describe("2005-07-14/a.png", """{"capturedAtUtc": "2015-06-01T10:00:00Z"}""");

        Process(s_lisbon);

        var manifest = ReadManifest();
        Assert.Equal((new DateTime(2015, 6, 1, 11, 0, 0), CaptureDateSource.Export), (manifest.CapturedAt, manifest.CapturedAtSource));
    }

    [Fact]
    public void Takes_a_wall_clock_export_time_as_it_is_whatever_the_zone()
    {
        _drive.Write("a.png", TestPhotos.Plain());
        Describe("a.png", """{"capturedAtWall": "2015-06-01T10:00:00"}""");

        Process(TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));

        Assert.Equal(new DateTime(2015, 6, 1, 10, 0, 0), ReadManifest().CapturedAt);
    }

    [Fact]
    public void Fills_in_the_location_only_when_the_file_has_none()
    {
        _drive.Write("none.png", TestPhotos.Plain());
        _drive.Write("gps.jpg", TestPhotos.Porto());
        Describe("none.png", """{"latitude": 38.72, "longitude": -9.14}""");
        Describe("gps.jpg", """{"latitude": 38.72, "longitude": -9.14}""");

        Process(s_lisbon);

        var byName = ReadManifests().ToDictionary(m => m.Sources.Single().Path);
        Assert.Equal(new GeoPoint(38.72, -9.14), byName["none.png"].Location);
        Assert.Equal(41.18, byName["gps.jpg"].Location!.Latitude, 2);
    }

    [Fact]
    public void Counts_coordinates_zero_zero_as_no_location()
    {
        _drive.Write("a.png", TestPhotos.Plain());
        Describe("a.png", """{"latitude": 0, "longitude": 0}""");

        Process(s_lisbon);

        Assert.Null(ReadManifest().Location);
    }

    [Fact]
    public void Writes_the_caption_and_the_albums_and_the_manifest_validates()
    {
        _drive.Write("a.png", TestPhotos.Plain());
        Describe("a.png", """{"caption": "  Praia de Matosinhos ", "albums": ["Viagem", "Ferias", "Viagem", ""]}""");

        Process(s_lisbon);

        var json = ReadManifestJson();
        Assert.Equal("Praia de Matosinhos", (string?)json["caption"]);
        Assert.Equal(["Ferias", "Viagem"], json["albums"]!.AsArray().Select(a => (string?)a));
        Assert.True(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out var error), error);
    }

    [Fact]
    public void Writes_nothing_new_for_a_file_the_export_does_not_mention()
    {
        _drive.Write("a.png", TestPhotos.Plain());
        Describe("other.png", """{"caption": "x"}""");

        Process(s_lisbon);

        var json = ReadManifestJson();
        Assert.False(json.ContainsKey("caption"));
        Assert.False(json.ContainsKey("albums"));
        Assert.Null(json["capturedAt"]);
    }

    [Fact]
    public void Leaves_out_the_files_the_reader_says_are_bookkeeping()
    {
        _drive.Write("a.png", TestPhotos.Plain());
        _drive.Write("thumbs/b.jpg", TestPhotos.Porto());
        _drive.Write("_export.json", "{\"files\": {}, \"leftOut\": [\"thumbs/b.jpg\"]}"u8.ToArray());

        Process(s_lisbon);

        Assert.Equal(["a.png"], ReadManifests().SelectMany(m => m.Sources).Select(s => s.Path));
    }

    [Fact]
    public void Adds_the_export_to_another_copy_of_the_same_bytes_and_merges_what_both_say()
    {
        var photo = TestPhotos.Plain();
        _drive.Write("one/a.png", photo);
        _drive.Write("two/a.png", photo);
        Describe("one/a.png", """{"caption": "curta", "albums": ["B"], "capturedAtUtc": "2016-06-01T10:00:00Z"}""");
        Describe("two/a.png", """{"caption": "uma legenda mais longa", "albums": ["A", "B"], "capturedAtUtc": "2015-06-01T10:00:00Z"}""");

        Process(s_lisbon);

        var manifest = ReadManifest();
        Assert.Equal(2, manifest.Sources.Count);
        Assert.Equal("uma legenda mais longa", manifest.Caption);
        Assert.Equal(["A", "B"], manifest.Albums);
        Assert.Equal(new DateTime(2015, 6, 1, 11, 0, 0), manifest.CapturedAt);
    }

    [Fact]
    public void Does_not_touch_a_file_of_the_export_or_add_one()
    {
        _drive.Write("a.png", TestPhotos.Plain());
        _drive.Write("2015/b.jpg", TestPhotos.Porto());
        Describe("a.png", """{"caption": "x", "capturedAtUtc": "2015-06-01T10:00:00Z"}""");
        var old = new DateTime(2016, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (var file in Directory.GetFiles(_drive.Path, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, old);
        }

        var before = Snapshot();

        Process(s_lisbon);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Behaves_exactly_as_before_without_export()
    {
        _drive.Write("a.png", TestPhotos.Plain());
        Describe("a.png", """{"caption": "x", "capturedAtUtc": "2015-06-01T10:00:00Z"}""");

        ProcessCommand.Run(new ProcessOptions(_drive.Path, _work.Path, "HD", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None));

        var json = ReadManifestJson();
        Assert.False(json.ContainsKey("caption"));
        Assert.Null(json["capturedAt"]);
    }

    [Fact]
    public void Stops_with_a_message_when_the_reader_cannot_read_the_export()
    {
        _drive.Write("_export.json", "{ not json"u8.ToArray());

        var ex = Assert.Throws<ExportReadException>(() => Process(s_lisbon));

        Assert.Contains("_export.json", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_work.Path, "manifests")) && Directory.GetFiles(Path.Combine(_work.Path, "manifests")).Length > 0);
    }

    [Fact]
    public void Refuses_a_kind_it_does_not_know_and_lists_the_known_ones()
    {
        var registry = Registry();

        Assert.False(ProcessOptions.TryParse(["/in", "--out", "/out", "--export", "instagram"], out _, out var error, registry));
        Assert.Equal("Unknown export kind 'instagram'. Known kinds: test.", error);
        Assert.False(ProcessOptions.TryParse(["/in", "--out", "/out", "--export", "test"], out _, out var none, new ExportReaderRegistry([])));
        Assert.Contains("Known kinds: none yet", none, StringComparison.Ordinal);
        Assert.False(ProcessOptions.TryParse(["/in", "--out", "/out", "--export"], out _, out var missing, registry));
        Assert.Equal("--export needs a value.", missing);
    }

    [Fact]
    public void Parses_the_kind_and_the_time_zone_and_runs_as_before_without_them()
    {
        Assert.True(ProcessOptions.TryParse(["/in", "--out", "/out", "--export", "test", "--time-zone", "Europe/Lisbon"], out var options, out _, Registry()));
        Assert.Equal(("test", "Europe/Lisbon"), (options.ExportKind, options.TimeZone!.Id));
        Assert.True(ProcessOptions.TryParse(["/in", "--out", "/out", "--export", "test"], out var defaults, out _, Registry()));
        Assert.Null(defaults.TimeZone);
        Assert.True(ProcessOptions.TryParse(["/in", "--out", "/out"], out var plain, out _));
        Assert.Equal((null, null), (plain.ExportKind, plain.TimeZone));
    }

    [Fact]
    public void Refuses_an_unknown_time_zone_and_a_time_zone_without_an_export()
    {
        Assert.False(ProcessOptions.TryParse(["/in", "--out", "/out", "--export", "test", "--time-zone", "Mars/Olympus"], out _, out var unknown, Registry()));
        Assert.Contains("Unknown time zone 'Mars/Olympus'", unknown, StringComparison.Ordinal);
        Assert.False(ProcessOptions.TryParse(["/in", "--out", "/out", "--time-zone", "Europe/Lisbon"], out _, out var alone, Registry()));
        Assert.Contains("needs --export", alone, StringComparison.Ordinal);
    }

    private static ExportReaderRegistry Registry() => new([new TestReader()]);

    private ProcessResult Process(TimeZoneInfo zone) =>
        ProcessCommand.Run(
            new ProcessOptions(_drive.Path, _work.Path, "HD", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None, ExportKind: "test", TimeZone: zone),
            embedder: null,
            embedderWarning: "no model in this test",
            hooks: new ProcessCommand.ProcessHooks(ExportReaders: Registry()));

    // Adds what the "export" says about one file to its sidecar (a JSON file in the folder, which is no media).
    private void Describe(string path, string json)
    {
        var sidecar = Path.Combine(_drive.Path, "_export.json");
        var root = File.Exists(sidecar) ? JsonNode.Parse(File.ReadAllText(sidecar))!.AsObject() : new JsonObject { ["files"] = new JsonObject() };
        root["files"]!.AsObject()[path] = JsonNode.Parse(json);
        File.WriteAllText(sidecar, root.ToJsonString());
    }

    private List<(string Path, long Size, DateTime Modified)> Snapshot() =>
        [.. Directory.GetFiles(_drive.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => (f[_drive.Path.Length..], new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))];

    private List<Manifest> ReadManifests() =>
        [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Select(f => ManifestJson.Deserialize(File.ReadAllText(f)))];

    private Manifest ReadManifest() => Assert.Single(ReadManifests());

    private JsonObject ReadManifestJson() =>
        JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json"))))!.AsObject();

    /// <summary>Reads <c>_export.json</c>: <c>files</c> by relative path and <c>leftOut</c>; the one thing a real reader would do.</summary>
    private sealed class TestReader : IExportReader
    {
        public string Kind => "test";

        public ExportScan Read(string root, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(root, "_export.json");
            if (!File.Exists(path))
            {
                return new ExportScan();
            }

            JsonObject document;
            try
            {
                document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ExportReadException("_export.json is not valid JSON.", ex);
            }

            var files = document["files"]?.AsObject().Select(p => new KeyValuePair<string, ExportMetadata>(p.Key, Parse(p.Value!.AsObject()))) ?? [];
            var leftOut = document["leftOut"]?.AsArray().Select(n => (string)n!) ?? [];
            return new ExportScan(files, leftOut);
        }

        private static ExportMetadata Parse(JsonObject node) => new()
        {
            CapturedAt = (string?)node["capturedAtUtc"] is { } utc
                ? DateTime.Parse(utc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal)
                : (string?)node["capturedAtWall"] is { } wall
                    ? DateTime.SpecifyKind(DateTime.Parse(wall, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Unspecified)
                    : null,
            Location = node["latitude"] is { } lat && node["longitude"] is { } lon ? new GeoPoint((double)lat, (double)lon) : null,
            Caption = (string?)node["caption"],
            Albums = node["albums"]?.AsArray().Select(a => (string)a!).ToList() ?? [],
        };
    }
}
