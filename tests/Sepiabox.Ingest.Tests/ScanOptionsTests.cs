using Sepiabox.Ingest.Scanning;

namespace Sepiabox.Ingest.Tests;

public class ScanOptionsTests
{
    [Fact]
    public void A_folder_alone_scans_with_duplicates_and_no_report()
    {
        Assert.True(ScanOptions.TryParse(["E:\\Fotos"], out var options, out _));
        Assert.Equal(new ScanOptions("E:\\Fotos", null, FindDuplicates: true), options);
    }

    [Fact]
    public void Reads_report_path_and_no_duplicates_in_any_order()
    {
        Assert.True(ScanOptions.TryParse(["--no-duplicates", "/media/hd", "--report", "scan.json"], out var options, out _));
        Assert.Equal(new ScanOptions("/media/hd", "scan.json", FindDuplicates: false), options);
    }

    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "--report" },
        new[] { "/media/hd", "--fast" },
        new[] { "/media/hd", "/media/other" },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Rejects_invalid_arguments(string[] args)
    {
        Assert.False(ScanOptions.TryParse(args, out var options, out var error));
        Assert.Null(options);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
