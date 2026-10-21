using System.Globalization;
using System.Text.Json.Nodes;
using Json.Schema;
using Sepiabox.Ingest.Reporting;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

/// <summary><c>sepiabox-ingest report</c> (S-062) over generated work folders.</summary>
public sealed class AssetReportTests
{
    [Fact]
    public void Counts_unique_assets_per_year_and_kind_the_undated_and_those_at_two_paths()
    {
        using var work = new TempFolder();
        Write(work, Asset(1, MediaKind.Photo, 2019, "DCIM/a.jpg", "Copia/a.jpg"));
        Write(work, Asset(2, MediaKind.Photo, 2019, "DCIM/b.jpg"));
        Write(work, Asset(3, MediaKind.Photo, 2019, "DCIM/c.jpg"));
        Write(work, Asset(4, MediaKind.Video, 2019, "DCIM/d.mp4"));
        Write(work, Asset(5, MediaKind.Video, 2019, "DCIM/e.mp4"));
        Write(work, Asset(6, MediaKind.Photo, 2020, "DCIM/f.jpg"));
        Write(work, Asset(7, MediaKind.Photo, null, "Scans/g.jpg"));
        Write(work, Asset(8, MediaKind.Video, null, "Fitas/h.mp4"));
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exit = ReportCommand.Run(new ReportOptions([work.Path], null), output);

        Assert.Equal(0, exit);
        var lines = output.ToString().Split(Environment.NewLine);
        Assert.Contains("2019             3         2         5", lines);
        Assert.Contains("2020             1         0         1", lines);
        Assert.Contains("Undated          1         1         2", lines);
        Assert.Contains("Total            5         3         8", lines);
        Assert.Contains("1 asset was found at more than one path.", lines);
    }

    [Theory]
    [InlineData(false, 2021)]
    [InlineData(true, 2018)]
    public void Counts_an_asset_in_two_work_folders_once_in_the_year_of_the_folder_given_last(bool reversed, int expectedYear)
    {
        using var first = new TempFolder();
        using var second = new TempFolder();
        Write(first, Asset(1, MediaKind.Photo, 2018, "HD/a.jpg"));
        Write(first, Asset(2, MediaKind.Video, 2018, "HD/b.mp4"));
        Write(second, Asset(1, MediaKind.Photo, 2021, "Celular/a.jpg"));
        string[] order = reversed ? [second.Path, first.Path] : [first.Path, second.Path];

        var report = AssetReport.Build(order, new ManifestValidator());

        Assert.Equal(2, report.Total);
        Assert.Equal(1, report.Years.Single(y => y.Year == expectedYear).Photos);
        Assert.Equal(1, report.Years.Single(y => y.Year == 2018).Videos);
        // The same bytes on two drives: two sources once merged, as in the cloud.
        Assert.Equal(1, report.MultipleSources);
    }

    [Fact]
    public void Takes_the_cloud_s_stronger_date_when_one_folder_only_knew_the_folder_s_day()
    {
        using var first = new TempFolder();
        using var second = new TempFolder();
        Write(first, Asset(1, MediaKind.Photo, 2015, "HD/a.jpg"));
        Write(second, Asset(1, MediaKind.Photo, 2003, "2003-05-01/a.jpg", source: CaptureDateSource.Folder));

        var report = AssetReport.Build([first.Path, second.Path], new ManifestValidator());

        // Metadata beats a folder's day whichever comes last (D-046), as ManifestMerge decides in the cloud.
        Assert.Equal(2015, Assert.Single(report.Years).Year);
    }

    [Fact]
    public void Writes_the_same_numbers_as_json_in_the_documented_shape()
    {
        using var work = new TempFolder();
        Write(work, Asset(1, MediaKind.Photo, 2019, "a.jpg", "b.jpg"));
        Write(work, Asset(2, MediaKind.Video, 2019, "c.mp4"));
        Write(work, Asset(3, MediaKind.Video, null, "d.mp4"));
        File.WriteAllText(Path.Combine(work.Path, "manifests", "broken.json"), "{}");
        var json = Path.Combine(work.Path, "report.json");

        var exit = ReportCommand.Run(new ReportOptions([work.Path], json), TextWriter.Null);

        Assert.Equal(1, exit);
        var document = JsonNode.Parse(File.ReadAllText(json))!;
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "schemas", "ingest-report.v1.json")));
        Assert.True(schema.Evaluate(document).IsValid);
        Assert.Equal(1, document["schemaVersion"]!.GetValue<int>());
        Assert.Equal(Path.GetFullPath(work.Path), document["workFolders"]![0]!.GetValue<string>());
        Assert.Equal(3, document["total"]!.GetValue<int>());
        Assert.Equal(
            """[{"year":2019,"photos":1,"videos":1,"total":2}]""",
            document["years"]!.ToJsonString());
        Assert.Equal("""{"photos":0,"videos":1,"total":1}""", document["undated"]!.ToJsonString());
        Assert.Equal(1, document["multipleSources"]!.GetValue<int>());
        Assert.EndsWith("broken.json", Assert.Single(document["problems"]!.AsArray())!["path"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Lists_an_invalid_manifest_as_a_problem_does_not_count_it_and_exits_with_1()
    {
        using var work = new TempFolder();
        Write(work, Asset(1, MediaKind.Photo, 2019, "a.jpg"));
        var broken = Path.Combine(work.Path, "manifests", Id(2) + ".json");
        File.WriteAllText(broken, ManifestJson.Serialize(Asset(2, MediaKind.Photo, 2019, "b.jpg")).Replace("\"photo\"", "\"drawing\"", StringComparison.Ordinal));
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exit = ReportCommand.Run(new ReportOptions([work.Path], null), output);

        Assert.Equal(1, exit);
        Assert.Contains("Total            1         0         1", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("1 manifests could not be read and are not counted:", output.ToString(), StringComparison.Ordinal);
        Assert.Contains($"  {broken}: ", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Changes_nothing_in_the_work_folder()
    {
        using var work = new TempFolder();
        Write(work, Asset(1, MediaKind.Photo, 2019, "a.jpg"));
        var before = Snapshot(work.Path);

        ReportCommand.Run(new ReportOptions([work.Path], null), TextWriter.Null);

        Assert.Equal(before, Snapshot(work.Path));
    }

    [Fact]
    public void Refuses_a_folder_that_is_not_a_work_folder()
    {
        using var folder = new TempFolder();

        Assert.Throws<DirectoryNotFoundException>(() => AssetReport.Build([folder.Path], new ManifestValidator()));
    }

    [Fact]
    public void Says_so_when_several_assets_have_more_than_one_source_and_when_none_do()
    {
        var none = new AssetReport(["w"], [], 0, 0, 0, []);
        var several = new AssetReport(["w"], [new YearCount(2019, 1200, 3)], 0, 0, 2, []);

        Assert.Contains("0 assets were found at more than one path.", Table(none), StringComparison.Ordinal);
        Assert.Contains("2019         1,200         3     1,203", Table(several), StringComparison.Ordinal);
        Assert.Contains("2 assets were found at more than one path.", Table(several), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "w1" }, null)]
    [InlineData(new[] { "w1", "w2", "--json", "r.json" }, "r.json")]
    [InlineData(new[] { "--json", "r.json", "w1" }, "r.json")]
    public void Reads_the_work_folders_in_order_and_the_json_path(string[] args, string? json)
    {
        Assert.True(ReportOptions.TryParse(args, out var options, out _));
        Assert.Equal(args.Where(a => a.StartsWith('w')), options.WorkFolders);
        Assert.Equal(json, options.JsonPath);
    }

    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "--json", "r.json" },
        new[] { "w1", "--json" },
        new[] { "w1", "--json", "a.json", "--json", "b.json" },
        new[] { "w1", "--fast" },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Rejects_invalid_arguments(string[] args)
    {
        Assert.False(ReportOptions.TryParse(args, out var options, out var error));
        Assert.Null(options);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private static string Table(AssetReport report)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        report.WriteTable(output);
        return output.ToString();
    }

    private static string Id(int n) => n.ToString("x64", CultureInfo.InvariantCulture);

    private static Manifest Asset(int n, MediaKind kind, int? year, string path, string? secondPath = null, CaptureDateSource source = CaptureDateSource.Metadata)
    {
        var id = Id(n);
        var photo = kind == MediaKind.Photo;
        return new Manifest
        {
            Id = id,
            Kind = kind,
            SizeBytes = 1000 + n,
            ContentType = photo ? "image/jpeg" : "video/mp4",
            Captured = year is null ? null : new CaptureDate(new DateTime(year.Value, 5, 1, 12, 0, 0), source),
            Sources = secondPath is null
                ? [TestManifests.Source("HD", path)]
                : [TestManifests.Source("HD", path), TestManifests.Source("HD", secondPath)],
            Derivatives = [new ManifestDerivative("thumb-256", $"derivatives/{id}/thumb-256.webp", 256, 192, "image/webp")],
        };
    }

    private static void Write(TempFolder work, Manifest manifest)
    {
        Directory.CreateDirectory(Path.Combine(work.Path, "manifests"));
        ManifestJson.WriteFile(Path.Combine(work.Path, "manifests", manifest.Id + ".json"), manifest);
    }

    private static Dictionary<string, (long, DateTime)> Snapshot(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => (new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)));

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sepiabox.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Sepiabox.slnx not found above the test output.");
    }
}
