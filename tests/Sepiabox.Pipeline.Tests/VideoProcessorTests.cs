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

    [Theory]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(3840, 2160, 1920, 1080)]
    [InlineData(1080, 1920, 1080, 1920)]
    [InlineData(2160, 3840, 1080, 1920)]
    [InlineData(4000, 3000, 1440, 1080)]
    [InlineData(1280, 720, 1280, 720)]
    [InlineData(640, 360, 640, 360)]
    [InlineData(641, 361, 640, 360)]
    [InlineData(3841, 2161, 1920, 1080)]
    [InlineData(1921, 1081, 1918, 1080)]
    public void Fits_into_1080p_in_either_orientation_without_upscaling_and_with_even_sides(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), VideoScale.Fit(width, height, VideoVersion.Hd1080.LongestSide, VideoVersion.Hd1080.ShortestSide));
    }

    [Fact]
    public void The_two_playable_versions_have_their_own_names_and_sizes()
    {
        Assert.Equal(("proxy-720p", 1280, 720), (VideoVersion.Hd720.Name, VideoVersion.Hd720.LongestSide, VideoVersion.Hd720.ShortestSide));
        Assert.Equal(("proxy-1080p", 1920, 1080), (VideoVersion.Hd1080.Name, VideoVersion.Hd1080.LongestSide, VideoVersion.Hd1080.ShortestSide));
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
    public void Builds_a_proxy_command_without_sound_when_asked()
    {
        var arguments = FfmpegArguments.Proxy("clip.mod", 640, 480, VideoEncoder.Software, 0, "out.mp4", withAudio: false);

        AssertSequence(arguments, "-map", "0:v:0", "-an");
        Assert.DoesNotContain("0:a:0?", arguments);
        Assert.DoesNotContain("-c:a", arguments);
        Assert.Equal("out.mp4", arguments[^1]);
    }

    [Fact]
    public void Keeps_the_first_sound_stream_in_the_proxy_by_default()
    {
        var arguments = FfmpegArguments.Proxy("clip.mod", 640, 480, VideoEncoder.Software, 0, "out.mp4");

        AssertSequence(arguments, "-map", "0:v:0", "-map", "0:a:0?");
        Assert.DoesNotContain("-an", arguments);
    }

    [Fact]
    public void Uses_h264_nvenc_when_asked()
    {
        var arguments = FfmpegArguments.Proxy("in.mov", 1280, 720, VideoEncoder.Nvenc, 0, "out.mp4");

        AssertSequence(arguments, "-c:v", "h264_nvenc");
        Assert.DoesNotContain("libx264", arguments);
    }

    [Theory]
    [InlineData(VideoEncoder.Software)]
    [InlineData(VideoEncoder.Nvenc)]
    public void Checks_the_encoder_with_a_generated_clip_and_exactly_the_720p_version_s_encoding(VideoEncoder encoder)
    {
        var check = FfmpegArguments.EncoderCheck(encoder, "check.mp4");
        var proxy = FfmpegArguments.Proxy("clip.mod", 640, 360, encoder, 0, "check.mp4");

        AssertSequence(check, "-f", "lavfi", "-i", "testsrc=size=640x360:rate=25:duration=0.5");
        AssertSequence(check, "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo");
        AssertSequence(check, "-map", "0:v:0", "-map", "1:a:0", "-t", "0.5");
        Assert.Equal(proxy.SkipWhile(a => a != "-map_metadata"), check.SkipWhile(a => a != "-map_metadata"));
        AssertSequence(check, [.. FfmpegArguments.VideoCodec(encoder)]);
    }

    [Fact]
    public void Uses_the_given_video_codec_arguments_instead_of_the_encoder_s()
    {
        var arguments = FfmpegArguments.Proxy("in.mov", 640, 360, VideoEncoder.Nvenc, 0, "out.mp4", videoCodec: ["-c:v", "libx264"]);

        AssertSequence(arguments, "-c:v", "libx264", "-pix_fmt");
        Assert.DoesNotContain("h264_nvenc", arguments);
    }

    [Fact]
    public void Decodes_only_the_first_frame_of_the_first_sound_stream_to_see_whether_the_sound_decodes()
    {
        var arguments = FfmpegArguments.SoundFrame("clip.mod");

        AssertSequence(arguments, "-i", "clip.mod", "-map", "0:a:0", "-frames:a", "1", "-f", "framecrc", "-");
        Assert.Equal(["-v", "error", "-select_streams", "a", "-show_entries", "stream=index", "-of", "csv=p=0", "clip.mod"], FfmpegArguments.SoundStreams("clip.mod"));
    }

    [Fact]
    public void Maps_the_sound_stream_it_is_asked_for_and_checks_that_one()
    {
        var proxy = FfmpegArguments.Proxy("clip.mpg", 640, 360, VideoEncoder.Software, 0, "out.mp4", audioStream: 2);
        var first = FfmpegArguments.Proxy("clip.mpg", 640, 360, VideoEncoder.Software, 0, "out.mp4");
        var none = FfmpegArguments.Proxy("clip.mpg", 640, 360, VideoEncoder.Software, 0, "out.mp4", withAudio: false, audioStream: 2);

        AssertSequence(proxy, "-map", "0:v:0", "-map", "0:a:2?");
        AssertSequence(first, "-map", "0:v:0", "-map", "0:a:0?");
        Assert.DoesNotContain("0:a:2?", none);
        Assert.Contains("-an", none);
        AssertSequence(FfmpegArguments.SoundFrame("clip.mpg", 1), "-i", "clip.mpg", "-map", "0:a:1", "-frames:a", "1");
    }

    [Theory]
    [InlineData("#software: Lavf60.16.100\n#tb 0: 1/48000\n#media_type 0: audio\n0,          0,          0,     1536,     6144, 0x3cd42723\n", 1)]
    [InlineData("#software: Lavf60.16.100\n#tb 0: 1/48000\n", 0)]
    [InlineData("", 0)]
    public void Counts_the_decoded_frames_without_the_header_lines(string framecrc, int frames)
    {
        Assert.Equal(frames, FfmpegArguments.CountFrames(framecrc));
    }

    [Theory]
    [InlineData("1\n", 1)]
    [InlineData("1\n2\n", 2)]
    [InlineData("", 0)]
    [InlineData("\n", 0)]
    public void Counts_the_sound_streams_ffprobe_lists(string output, int streams)
    {
        Assert.Equal(streams, FfmpegArguments.CountStreams(output));
    }

    [Fact]
    public void Keeps_every_line_of_ffmpeg_s_errors_so_the_cause_shows_and_not_only_its_consequence()
    {
        // The owner's NVENC failure (D-087): the last line alone reads like a sound problem.
        const string Errors = """
            [h264_nvenc @ 0x1] Driver does not support the required nvenc API version. Required: 13.1 Found: 13.0
            [h264_nvenc @ 0x1] The minimum required Nvidia driver for nvenc is 610.00 or newer
            [vost#0:0/h264_nvenc @ 0x2] Error while opening encoder - maybe incorrect parameters such as bit_rate, rate, width or height.
            [out#0/mp4 @ 0x3] Nothing was written into output file, because at least one of its streams received no packets.

            """;

        var message = FfmpegArguments.Message(Errors);

        Assert.StartsWith("[h264_nvenc @ 0x1] Driver does not support the required nvenc API version. Required: 13.1 Found: 13.0 | ", message, StringComparison.Ordinal);
        Assert.EndsWith(" | [out#0/mp4 @ 0x3] Nothing was written into output file, because at least one of its streams received no packets.", message, StringComparison.Ordinal);
        Assert.Equal(4, message.Split(" | ").Length);
    }

    [Fact]
    public void Keeps_both_ends_of_a_very_long_ffmpeg_message()
    {
        var message = FfmpegArguments.Message(string.Join('\n', Enumerable.Range(1, 500).Select(i => $"line {i}")));

        Assert.Equal(FfmpegArguments.MaxMessageLength, message.Length);
        // The cause is usually the first line, the outcome the last.
        Assert.StartsWith("line 1 | line 2 | ", message, StringComparison.Ordinal);
        Assert.Equal('…', message[300]);
        Assert.EndsWith(" | line 500", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Error opening input file /media/HD/MOV001.MOD.\nError opening input files: Input/output error", true)]
    [InlineData("[in#0 @ 0x1] Error opening input: No such file or directory", true)]
    [InlineData("/media/HD/MOV001.MOD: Permission denied", true)]
    [InlineData("[aist#0:1/ac3 @ 0x1] Error submitting packet to decoder: Invalid data found when processing input\n[out#0/framecrc @ 0x2] Nothing was written into output file, because at least one of its streams received no packets.", false)]
    [InlineData("", false)]
    public void Tells_a_file_that_could_not_be_read_from_sound_that_does_not_decode(string errors, bool inputFailure)
    {
        Assert.Equal(inputFailure, FfmpegArguments.IsInputFailure(errors));
    }

    [Fact]
    public void Allows_fewer_nvenc_sessions_than_a_consumer_driver_does()
    {
        Assert.InRange(VideoProcessor.MaxNvencSessions, 1, 7);
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
    public void Asks_ffprobe_for_the_pixel_aspect_ratio_and_the_location_tags()
    {
        AssertSequence(
            FfmpegArguments.Probe("clip.mod"),
            "-select_streams", "v",
            "-show_entries", "stream=width,height,sample_aspect_ratio:stream_disposition=attached_pic:stream_tags=rotate:stream_side_data=rotation:format=duration:format_tags=com.apple.quicktime.location.ISO6709,location");
    }

    [Theory]
    [InlineData("""{ "location": "+38.7223-009.1393+10.000000/" }""", 38.7223, -9.1393)]
    [InlineData("""{ "com.apple.quicktime.location.ISO6709": "+38.7223-009.1393+010.000/" }""", 38.7223, -9.1393)]
    [InlineData("""{ "location": "+41.1800-008.6900/", "com.apple.quicktime.location.ISO6709": "+38.7223-009.1393+010.000/" }""", 38.7223, -9.1393)]
    [InlineData("""{ "location": "+41.1800-008.6900/", "com.apple.quicktime.location.ISO6709": "+00.0000+000.0000/" }""", 41.18, -8.69)]
    public void Reads_the_location_from_the_format_tags(string tags, double latitude, double longitude)
    {
        var json = $$"""{ "streams": [ { "width": 1920, "height": 1080 } ], "format": { "tags": {{tags}} } }""";

        Assert.Equal(new GeoPoint(latitude, longitude), FfmpegArguments.ParseProbe(json).Location);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{ "tags": { "location": "+00.0000+000.0000/" } }""")]
    [InlineData("""{ "tags": { "location": "somewhere" } }""")]
    [InlineData("""{ "tags": { "location": "+\u0663\u0668.7223-009.1393/" } }""")]
    [InlineData("""{ "tags": { "location": 38 } }""")]
    [InlineData("""{ "tags": [] }""")]
    public void Has_no_location_when_the_format_tags_hold_none(string format)
    {
        var json = $$"""{ "streams": [ { "width": 1920, "height": 1080 } ], "format": {{format}} }""";

        Assert.Null(FfmpegArguments.ParseProbe(json).Location);
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
