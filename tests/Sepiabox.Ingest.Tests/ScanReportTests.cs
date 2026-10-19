using System.Text;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

public class ScanReportTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1_500, "1.5 KB")]
    [InlineData(398_200_000_000, "398.2 GB")]
    [InlineData(1_002_000_000_000, "1.0 TB")]
    public void Formats_sizes_in_decimal_units(long bytes, string expected) =>
        Assert.Equal(expected, ScanReport.FormatBytes(bytes));

    [Fact]
    public void Summary_shows_totals_years_duplicates_and_problems()
    {
        var output = new StringWriter();

        ScanReport.WriteSummary(SampleResult(), output);

        var text = output.ToString();
        Assert.Contains("Photos        12,000", text);
        Assert.Contains("2019", text);
        Assert.Contains("1 extra copy in 1 group, 2.0 MB", text);
        Assert.Contains("1 file could not be read", text);
        Assert.Contains("/drive/broken.jpg", text);
    }

    [Fact]
    public void Json_report_includes_every_duplicate_path()
    {
        using var stream = new MemoryStream();

        ScanReport.WriteJson(SampleResult(), stream);

        var json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"/drive/a.jpg\"", json);
        Assert.Contains("\"/drive/b.jpg\"", json);
        Assert.Contains("\"photo\"", json);
    }

    private static ScanResult SampleResult() => new()
    {
        Root = "/drive",
        Duration = TimeSpan.FromMinutes(3),
        Totals = new Dictionary<MediaKind, KindTotals>
        {
            [MediaKind.Photo] = new(12_000, 40_000_000_000),
            [MediaKind.Video] = new(300, 60_000_000_000),
            [MediaKind.Other] = new(20, 1_000_000),
        },
        MediaByYear = new SortedDictionary<string, int> { ["2019"] = 12_000, [ScanResult.UnknownYear] = 300 },
        FilesByExtension = new Dictionary<string, int> { [".jpg"] = 12_000 },
        Duplicates = [new DuplicateGroup(2_000_000, ["/drive/a.jpg", "/drive/b.jpg"])],
        Problems = [new ScanProblem("/drive/broken.jpg", "Input/output error")],
    };
}
