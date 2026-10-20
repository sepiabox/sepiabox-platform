using Sepiabox.Pipeline.Media;
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
        var arguments = FfmpegArguments.Proxy("in put.mov", 1280, 720, VideoEncoder.Software, 0, "out.mp4");

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
        var arguments = FfmpegArguments.Proxy("in.mov", 1280, 720, VideoEncoder.Nvenc, 0, "out.mp4");

        AssertSequence(arguments, "-c:v", "h264_nvenc");
        Assert.DoesNotContain("libx264", arguments);
    }

    [Fact]
    public void Takes_the_poster_frame_at_the_given_second_with_invariant_formatting()
    {
        var arguments = FfmpegArguments.Frame("in.mov", 0.3, 1024, 576, 0, "frame.png");

        AssertSequence(arguments, "-ss", "0.3", "-i", "in.mov");
        AssertSequence(arguments, "-frames:v", "1");
    }

    [Fact]
    public void Deinterlaces_and_resamples_the_poster_frame_to_square_pixels_at_the_display_size()
    {
        var arguments = FfmpegArguments.Frame("clip.mod", 1, 1024, 576, 0, "frame.png");

        AssertSequence(arguments, "-vf", "yadif=deint=interlaced,scale=1024:576,setsar=1");
    }

    [Fact]
    public void Asks_ffprobe_for_the_pixel_aspect_ratio()
    {
        AssertSequence(
            FfmpegArguments.Probe("clip.mod"),
            "-select_streams", "v",
            "-show_entries", "stream=width,height,sample_aspect_ratio:stream_disposition=attached_pic:stream_tags=rotate:stream_side_data=rotation:format=duration");
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
    public void Maps_the_video_stream_that_holds_the_footage_when_a_cover_comes_first()
    {
        AssertSequence(FfmpegArguments.Frame("in.mkv", 1, 640, 360, videoStream: 1, "frame.png"), "-map", "0:v:1");
        AssertSequence(FfmpegArguments.Proxy("in.mkv", 640, 360, VideoEncoder.Software, videoStream: 1, "out.mp4"), "-map", "0:v:1", "-map", "0:a:0?");
    }

    [Fact]
    public void Skips_cover_art_when_choosing_the_video_stream()
    {
        const string Json = """
            { "streams": [
                { "width": 600, "height": 600, "disposition": { "attached_pic": 1 } },
                { "width": 1920, "height": 1080, "disposition": { "attached_pic": 0 } } ],
              "format": {} }
            """;

        var probe = FfmpegArguments.ParseProbe(Json);

        Assert.Equal(new VideoProbe(1920, 1080, null, VideoStream: 1), probe);
    }

    [Fact]
    public void Finds_no_video_when_the_only_picture_is_cover_art()
    {
        const string Json = """{ "streams": [ { "width": 600, "height": 600, "disposition": { "attached_pic": 1 } } ], "format": { "duration": "180.0" } }""";

        Assert.Equal(NoVideoReason.CoverArtOnly, Assert.Throws<NoVideoStreamException>(() => FfmpegArguments.ParseProbe(Json)).Reason);
    }

    [Fact]
    public void Reads_a_larger_part_of_the_file_when_asked_for_a_thorough_probe()
    {
        var arguments = FfmpegArguments.Probe("clip.mod", thorough: true);

        AssertSequence(arguments, "-probesize", "100M", "-analyzeduration", "100M");
        Assert.DoesNotContain("-probesize", FfmpegArguments.Probe("clip.mod"));
    }

    [Theory]
    [InlineData("""{ "streams": [ { "height": 1080 } ], "format": {} }""")]
    [InlineData("""{ "streams": [ { "width": 1920 } ], "format": {} }""")]
    [InlineData("""{ "streams": [ { "width": 0, "height": 0 } ], "format": {} }""")]
    [InlineData("""{ "streams": [ { "width": "N/A", "height": 1080 } ], "format": {} }""")]
    [InlineData("""{ "streams": [ 7 ], "format": {} }""")]
    [InlineData("""{ "streams": [ { "width": 1920, "height": 1080, "side_data_list": 3 } ], "format": {} }""")]
    [InlineData("not json")]
    public void Rejects_a_video_stream_without_a_picture_size(string json)
    {
        Assert.Throws<VideoProcessingException>(() => FfmpegArguments.ParseProbe(json));
    }

    [Fact]
    public void Rejects_a_file_without_a_video_stream()
    {
        Assert.Equal(NoVideoReason.NoStream, Assert.Throws<NoVideoStreamException>(() => FfmpegArguments.ParseProbe("""{ "streams": [], "format": {} }""")).Reason);
    }

    [UnixFact]
    public void Probes_again_with_a_larger_window_when_the_first_probe_gives_no_picture_size()
    {
        using var temp = new TempFolder();
        // As FFmpeg reports an MPEG file that starts with a minute of sound: the video stream, but no size yet.
        var ffprobe = TestTools.FakeTool(temp, "ffprobe", """
            for argument; do
              if [ "$argument" = "-probesize" ]; then echo '{ "streams": [ { "width": 720, "height": 576 } ], "format": { "duration": "90.0" } }'; exit 0; fi
            done
            echo '{ "streams": [ { "width": 0, "height": 0 } ], "format": {} }'
            """);
        var clip = temp.Write("clip.mpg", [1, 2, 3, 4]);

        var probe = new VideoProcessor(new FfmpegTools("ffmpeg", ffprobe)).Probe(clip);

        Assert.Equal(new VideoProbe(720, 576, 90), probe);
    }

    [UnixTheory]
    // A program stream pack header, another MPEG start code (a sequence header), transport stream sync bytes
    // every 188 bytes, and AVCHD's time-stamped 192-byte packets.
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0xBA }, 0)]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0xB3 }, 0)]
    [InlineData(new byte[] { 0x47 }, 188)]
    [InlineData(new byte[] { 0, 0, 0, 0, 0x47 }, 192)]
    public void Reports_an_mpeg_file_without_video_as_damaged_rather_than_leaving_it_out(byte[] start, int packet)
    {
        using var temp = new TempFolder();
        var ffprobe = TestTools.FakeTool(temp, "ffprobe", """echo '{ "streams": [], "format": {} }'""");
        var bytes = new byte[4096];
        start.CopyTo(bytes, 0);
        if (packet > 0)
        {
            bytes[start.Length - 1 + packet] = 0x47;
            bytes[start.Length - 1 + (2 * packet)] = 0x47;
        }

        var clip = temp.Write("MOV001.MOD", bytes);

        var error = Assert.Throws<VideoProcessingException>(() => new VideoProcessor(new FfmpegTools("ffmpeg", ffprobe)).Probe(clip));
        Assert.Contains("may be damaged", error.Message, StringComparison.Ordinal);
    }

    [UnixFact]
    public void Reports_a_file_as_damaged_when_the_first_probe_saw_a_video_stream_and_the_longer_one_none()
    {
        using var temp = new TempFolder();
        var ffprobe = TestTools.FakeTool(temp, "ffprobe", """
            for argument; do
              if [ "$argument" = "-probesize" ]; then echo '{ "streams": [], "format": {} }'; exit 0; fi
            done
            echo '{ "streams": [ { "width": 0, "height": 0 } ], "format": {} }'
            """);
        // Not MPEG-looking, so only what the first probe saw keeps it from being left out.
        var clip = temp.Write("clip.mp4", new byte[4096]);

        var error = Assert.Throws<VideoProcessingException>(() => new VideoProcessor(new FfmpegTools("ffmpeg", ffprobe)).Probe(clip));

        Assert.Contains("may be damaged", error.Message, StringComparison.Ordinal);
    }

    [UnixFact]
    public void Reports_a_problem_rather_than_leaving_out_a_file_whose_start_cannot_be_read()
    {
        using var temp = new TempFolder();
        var ffprobe = TestTools.FakeTool(temp, "ffprobe", """echo '{ "streams": [], "format": {} }'""");
        // A folder named like a video: the stand-in ffprobe answers, but the file's start cannot be read.
        var unreadable = Path.Combine(temp.Path, "clip.mod");
        Directory.CreateDirectory(unreadable);

        var error = Assert.Throws<VideoProcessingException>(() => new VideoProcessor(new FfmpegTools("ffmpeg", ffprobe)).Probe(unreadable));

        Assert.StartsWith("Could not read the start of the file", error.Message, StringComparison.Ordinal);
    }

    [UnixFact]
    public void Leaves_out_a_file_that_does_not_look_like_mpeg_when_no_probe_finds_video()
    {
        using var temp = new TempFolder();
        var probes = Path.Combine(temp.Path, "probes");
        var ffprobe = TestTools.FakeTool(temp, "ffprobe", $$"""
            echo x >> '{{probes}}'
            echo '{ "streams": [], "format": {} }'
            """);
        var clip = temp.Write("sound.mod", new byte[4096]);

        var error = Assert.Throws<NoVideoStreamException>(() => new VideoProcessor(new FfmpegTools("ffmpeg", ffprobe)).Probe(clip));

        Assert.Equal(NoVideoReason.NoStream, error.Reason);
        Assert.Contains("even with a longer probe", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, File.ReadAllLines(probes).Length);
    }

    [UnixFact]
    public void Does_not_probe_cover_art_again()
    {
        using var temp = new TempFolder();
        var probes = Path.Combine(temp.Path, "probes");
        var ffprobe = TestTools.FakeTool(temp, "ffprobe", $$"""
            echo x >> '{{probes}}'
            echo '{ "streams": [ { "width": 600, "height": 600, "disposition": { "attached_pic": 1 } } ], "format": {} }'
            """);
        var song = temp.Write("song.mp4", new byte[4096]);

        var error = Assert.Throws<NoVideoStreamException>(() => new VideoProcessor(new FfmpegTools("ffmpeg", ffprobe)).Probe(song));

        Assert.Equal(NoVideoReason.CoverArtOnly, error.Reason);
        Assert.Single(File.ReadAllLines(probes));
    }

    [UnixFact]
    public void Does_not_probe_again_when_ffprobe_fails()
    {
        using var temp = new TempFolder();
        var probes = Path.Combine(temp.Path, "probes");
        var ffprobe = TestTools.FakeTool(temp, "ffprobe", $$"""
            echo x >> '{{probes}}'
            echo 'Invalid data found when processing input' >&2
            exit 1
            """);
        var broken = temp.Write("broken.mod", new byte[4096]);

        Assert.Throws<VideoProcessingException>(() => new VideoProcessor(new FfmpegTools("ffmpeg", ffprobe)).Probe(broken));
        Assert.Single(File.ReadAllLines(probes));
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
