using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Reporting;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// <c>process</c> then <c>report</c> on the generated fixture library (S-061, S-062): report's numbers are the
/// library's known ones, in unique assets, which is what S-054 compares the catalog's answers with.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReportFixtureTests
{
    private const int Seed = 62;

    private static readonly FfmpegTools s_tools = FfmpegTools.Find(null)
        ?? throw new InvalidOperationException("Integration tests need ffmpeg and ffprobe on the PATH.");

    [Fact]
    public void Counts_the_fixture_library_s_known_numbers_after_process()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        var expected = FixtureLibrary.Write(drive.Path, Seed, s_tools.Ffmpeg);
        Process(drive, work, "HD");

        var report = AssetReport.Build([work.Path], new ManifestValidator());

        Assert.Empty(report.Problems);
        Assert.Equal(
            expected.ByYearAndKind,
            [.. report.Years.SelectMany(y => new[] { KeyValuePair.Create((y.Year, MediaKind.Photo), y.Photos), KeyValuePair.Create((y.Year, MediaKind.Video), y.Videos) })
                .Where(e => e.Value > 0)]);
        Assert.Equal((expected.UndatedOf(MediaKind.Photo), expected.UndatedOf(MediaKind.Video)), (report.UndatedPhotos, report.UndatedVideos));
        Assert.Equal(expected.Assets.Count, report.Total);
        // The exact duplicate is one asset at two paths.
        Assert.Equal(expected.Duplicates, report.MultipleSources);
    }

    [Fact]
    public void Counts_a_drive_processed_into_two_work_folders_once()
    {
        using var drive = new TempFolder();
        using var first = new TempFolder();
        using var second = new TempFolder();
        var expected = FixtureLibrary.Write(drive.Path, Seed, s_tools.Ffmpeg);
        Process(drive, first, "HD");
        Process(drive, second, "HD copy");

        var report = AssetReport.Build([first.Path, second.Path], new ManifestValidator());

        Assert.Equal(expected.Assets.Count, report.Total);
        Assert.Equal(expected.Undated, report.Undated);
        // Every asset now has a source on each "drive".
        Assert.Equal(expected.Assets.Count, report.MultipleSources);
    }

    private static void Process(TempFolder drive, TempFolder work, string source) =>
        ProcessCommand.Run(new ProcessOptions(
            drive.Path, work.Path, source, FfmpegFolder: Path.GetDirectoryName(s_tools.Ffmpeg), ModelFolder: TestModels.None));
}
