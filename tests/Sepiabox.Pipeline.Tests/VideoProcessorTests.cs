using Sepiabox.Pipeline.Video;

namespace Sepiabox.Pipeline.Tests;

public class VideoProcessorTests
{
    [Theory]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(3840, 2160, 1280, 720)]
    [InlineData(1080, 1920, 720, 1280)]
    [InlineData(4000, 3000, 960, 720)]
    [InlineData(640, 360, 640, 360)]
    [InlineData(641, 361, 640, 360)]
    public void Fits_into_720p_without_upscaling_and_with_even_sides(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), VideoScale.Fit(width, height));
    }

    [Fact]
    public void Builds_a_software_proxy_command_that_strips_metadata()
    {
        var arguments = FfmpegArguments.Proxy("in put.mov", 1280, 720, VideoEncoder.Software, "out.mp4");

        AssertSequence(arguments, "-i", "in put.mov");
        AssertSequence(arguments, "-vf", "yadif=deint=interlaced,scale=1280:720,setsar=1");
        AssertSequence(arguments, "-c:v", "libx264", "-preset", "veryfast", "-crf", "28");
        AssertSequence(arguments, "-c:a", "aac", "-b:a", "96k");
        AssertSequence(arguments, "-movflags", "+faststart");
        AssertSequence(arguments, "-map_metadata", "-1");
        Assert.Equal("out.mp4", arguments[^1]);
    }

    [Fact]
    public void Uses_h264_nvenc_when_asked()
    {
        var arguments = FfmpegArguments.Proxy("in.mov", 1280, 720, VideoEncoder.Nvenc, "out.mp4");

        AssertSequence(arguments, "-c:v", "h264_nvenc");
        Assert.DoesNotContain("libx264", arguments);
    }

    [Fact]
    public void Takes_the_poster_frame_at_the_given_second_with_invariant_formatting()
    {
        var arguments = FfmpegArguments.Frame("in.mov", 0.3, 1024, 576, "frame.png");

        AssertSequence(arguments, "-ss", "0.3", "-i", "in.mov");
        AssertSequence(arguments, "-frames:v", "1");
    }

    [Fact]
    public void Deinterlaces_and_resamples_the_poster_frame_to_square_pixels_at_the_display_size()
    {
        var arguments = FfmpegArguments.Frame("clip.mod", 1, 1024, 576, "frame.png");

        AssertSequence(arguments, "-vf", "yadif=deint=interlaced,scale=1024:576,setsar=1");
    }

    [Fact]
    public void Asks_ffprobe_for_the_pixel_aspect_ratio()
    {
        Assert.Contains("sample_aspect_ratio", FfmpegArguments.Probe("clip.mod")[5]);
    }

    [Theory]
    [InlineData(720, 576, "64:45", 1024)]
    [InlineData(720, 576, "16:15", 768)]
    [InlineData(720, 480, "8:9", 640)]
    [InlineData(720, 480, "32:27", 853)]
    [InlineData(1440, 1080, "4:3", 1920)]
    [InlineData(1920, 1080, "1:1", 1920)]
    [InlineData(720, 576, "0:1", 720)]
    [InlineData(720, 576, "N/A", 720)]
    [InlineData(720, 576, "1000:1", 720)]
    public void Applies_the_pixel_aspect_ratio_to_the_width(int width, int height, string ratio, int expectedWidth)
    {
        var json = $$"""{ "streams": [ { "width": {{width}}, "height": {{height}}, "sample_aspect_ratio": "{{ratio}}" } ], "format": {} }""";

        Assert.Equal(new VideoProbe(expectedWidth, height, null), FfmpegArguments.ParseProbe(json));
    }

    [Fact]
    public void Applies_the_pixel_aspect_ratio_before_rotation()
    {
        const string Json = """{ "streams": [ { "width": 1440, "height": 1080, "sample_aspect_ratio": "4:3", "tags": { "rotate": "90" } } ], "format": {} }""";

        Assert.Equal(new VideoProbe(1080, 1920, null), FfmpegArguments.ParseProbe(Json));
    }

    [Fact]
    public void Reads_size_and_duration_from_ffprobe()
    {
        const string Json = """{ "streams": [ { "width": 1920, "height": 1080 } ], "format": { "duration": "3.016000" } }""";

        Assert.Equal(new VideoProbe(1920, 1080, 3.016), FfmpegArguments.ParseProbe(Json));
    }

    [Theory]
    [InlineData("""{ "streams": [ { "width": 1920, "height": 1080, "side_data_list": [ { "rotation": -90 } ] } ], "format": {} }""")]
    [InlineData("""{ "streams": [ { "width": 1920, "height": 1080, "tags": { "rotate": "90" } } ], "format": {} }""")]
    public void Swaps_width_and_height_for_a_phone_video_recorded_upright(string json)
    {
        Assert.Equal(new VideoProbe(1080, 1920, null), FfmpegArguments.ParseProbe(json));
    }

    [Fact]
    public void Rejects_a_file_without_a_video_stream()
    {
        Assert.Throws<VideoProcessingException>(() => FfmpegArguments.ParseProbe("""{ "streams": [], "format": {} }"""));
    }

    [Fact]
    public void Finds_no_tools_in_a_folder_without_ffmpeg()
    {
        using var temp = new TempFolder();

        Assert.Null(FfmpegTools.Find(temp.Path));
    }

    private static void AssertSequence(IReadOnlyList<string> arguments, params string[] expected)
    {
        for (var i = 0; i + expected.Length <= arguments.Count; i++)
        {
            if (arguments.Skip(i).Take(expected.Length).SequenceEqual(expected))
            {
                return;
            }
        }

        Assert.Fail($"Expected '{string.Join(' ', expected)}' in '{string.Join(' ', arguments)}'.");
    }
}
