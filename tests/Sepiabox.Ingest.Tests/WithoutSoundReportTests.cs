using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Scanning;

namespace Sepiabox.Ingest.Tests;

public class WithoutSoundReportTests
{
    [Fact]
    public void Lists_each_video_made_without_sound_with_ffmpegs_message()
    {
        using var output = new StringWriter();

        WithoutSoundReport.Write(
            [
                new ScanProblem("/drive/MOV001.MOD", "ffmpeg failed: at least one of its streams received no packets."),
                new ScanProblem("/drive/party.mp4", "ffmpeg failed: OpenEncodeSessionEx failed: out of memory"),
            ],
            output);

        Assert.Equal(
            [
                "2 videos' 720p versions were made without sound, because the transcode with sound failed:",
                "  Made without sound /drive/MOV001.MOD: ffmpeg failed: at least one of its streams received no packets.",
                "  Made without sound /drive/party.mp4: ffmpeg failed: OpenEncodeSessionEx failed: out of memory",
            ],
            output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Says_one_video_in_the_singular()
    {
        using var output = new StringWriter();

        WithoutSoundReport.Write([new ScanProblem("/drive/MOV001.MOD", "ffmpeg failed: x")], output);

        Assert.StartsWith("1 video's 720p version was made without sound", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_nothing_when_every_video_kept_its_sound()
    {
        using var output = new StringWriter();

        WithoutSoundReport.Write([], output);

        Assert.Empty(output.ToString());
    }
}
