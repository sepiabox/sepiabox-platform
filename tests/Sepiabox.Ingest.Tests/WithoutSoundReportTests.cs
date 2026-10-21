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
                new ScanProblem("/drive/MOV002.MOD", "ffmpeg failed: Decode error rate 1 exceeds maximum 0.666667"),
            ],
            output);

        Assert.Equal(
            [
                "2 videos' 720p versions were made without sound, because FFmpeg cannot decode their sound:",
                "  Made without sound /drive/MOV001.MOD: ffmpeg failed: at least one of its streams received no packets.",
                "  Made without sound /drive/MOV002.MOD: ffmpeg failed: Decode error rate 1 exceeds maximum 0.666667",
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

    [Fact]
    public void Tells_the_owner_to_update_the_nvidia_driver_or_run_without_nvenc_when_its_encoder_cannot_encode()
    {
        const string Ffmpeg = "ffmpeg failed: [h264_nvenc @ 0x1] Driver does not support the required nvenc API version. Required: 13.1 Found: 13.0 | "
            + "[h264_nvenc @ 0x1] The minimum required Nvidia driver for nvenc is 610.00 or newer | Error while opening encoder";
        var failure = new Sepiabox.Pipeline.Video.VideoEncoderUnavailableException(
            Sepiabox.Pipeline.Video.VideoEncoder.Nvenc, Ffmpeg, new InvalidOperationException());

        var message = EncoderCheckReport.Message(failure);

        Assert.Contains("nothing was processed", message, StringComparison.Ordinal);
        Assert.Contains(Ffmpeg, message, StringComparison.Ordinal);
        Assert.Contains("Update the NVIDIA driver", message, StringComparison.Ordinal);
        Assert.Contains("without --hw nvenc", message, StringComparison.Ordinal);
    }
}
