using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

public class LeftOutReportTests
{
    private static readonly LeftOutFile[] s_files =
    [
        new("/drive/song.mod", NoVideoReason.TrackerModule, Earlier: false),
        new("/drive/sound.mod", NoVideoReason.NoStream, Earlier: false),
        new("/drive/old.mod", NoVideoReason.CoverArtOnly, Earlier: true),
        new("/drive/older.mod", null, Earlier: true),
    ];

    [Fact]
    public void Lists_files_left_out_in_this_run_with_their_reason_and_counts_earlier_ones()
    {
        using var output = new StringWriter();

        LeftOutReport.Write(s_files, listEarlier: false, output);

        Assert.Equal(
            [
                "2 files with a video extension hold no video and were left out:",
                "  Left out /drive/song.mod: tracker-music module (ProTracker format tag at offset 1080), not a video",
                "  Left out /drive/sound.mod: no video stream, even with a longer probe",
                "2 files left out by earlier runs are unchanged and were not read again; run process with --retry-left-out to read them again, or --list-left-out to list them.",
            ],
            Lines(output));
    }

    [Fact]
    public void Lists_earlier_files_too_when_asked()
    {
        using var output = new StringWriter();

        LeftOutReport.Write([s_files[0], s_files[2]], listEarlier: true, output);

        Assert.Equal(
            [
                "1 file with a video extension holds no video and was left out:",
                "  Left out /drive/song.mod: tracker-music module (ProTracker format tag at offset 1080), not a video",
                "  Left out /drive/old.mod: only cover art, no video stream (earlier run)",
                "1 file left out by an earlier run is unchanged and was not read again; run process with --retry-left-out to read them again.",
            ],
            Lines(output));
    }

    [Fact]
    public void Writes_nothing_when_nothing_was_left_out()
    {
        using var output = new StringWriter();

        LeftOutReport.Write([], listEarlier: true, output);

        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Says_the_reason_was_not_recorded_when_a_row_has_none()
    {
        Assert.Equal("no video (reason not recorded)", LeftOutReport.Describe(null));
        Assert.Equal("no video (reason not recorded)", LeftOutReport.Describe(NoVideoReason.Unknown));
    }

    private static string[] Lines(StringWriter output) =>
        output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
