using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Tests;

public class ProcessOptionsTests
{
    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "E:\\" },
        new[] { "--out", "work" },
        new[] { "E:\\", "--out" },
        new[] { "E:\\", "--out", "work", "--source-name" },
        new[] { "E:\\", "F:\\", "--out", "work" },
        new[] { "E:\\", "--out", "work", "--fast" },
        new[] { "E:\\", "--out", "work", "--ffmpeg" },
        new[] { "E:\\", "--out", "work", "--hw", "quicksync" },
        new[] { "E:\\", "--out", "work", "--models" },
        // The shipped ONNX Runtime package runs on the CPU only (D-042).
        new[] { "E:\\", "--out", "work", "--gpu" },
    };

    [Fact]
    public void Reads_the_models_folder()
    {
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work", "--models", "D:\\models"], out var options, out _));
        Assert.Equal("D:\\models", options.ModelFolder);
    }

    [Fact]
    public void Uses_the_default_models_folder_by_default()
    {
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work"], out var options, out _));
        Assert.Null(options.ModelFolder);
    }

    [Fact]
    public void Reads_the_ffmpeg_folder_and_the_nvenc_encoder()
    {
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work", "--ffmpeg", "C:\\ffmpeg\\bin", "--hw", "nvenc"], out var options, out _));
        Assert.Equal("C:\\ffmpeg\\bin", options.FfmpegFolder);
        Assert.Equal(VideoEncoder.Nvenc, options.Encoder);
    }

    [Fact]
    public void Reads_the_left_out_flags_and_leaves_them_off_by_default()
    {
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work", "--retry-left-out", "--list-left-out"], out var options, out _));
        Assert.True(options.RetryLeftOut);
        Assert.True(options.ListLeftOut);
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work"], out var defaults, out _));
        Assert.False(defaults.RetryLeftOut || defaults.ListLeftOut);
    }

    [Fact]
    public void Uses_the_software_encoder_and_the_path_by_default()
    {
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work"], out var options, out _));
        Assert.Null(options.FfmpegFolder);
        Assert.Equal(VideoEncoder.Software, options.Encoder);
    }

    [Fact]
    public void Reads_folder_output_and_source_name()
    {
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work", "--source-name", "HD da Ana"], out var options, out _));
        Assert.Equal(new ProcessOptions("E:\\", "work", "HD da Ana"), options);
    }

    [Fact]
    public void Leaves_the_source_name_empty_when_not_given()
    {
        Assert.True(ProcessOptions.TryParse(["--out", "work", "E:\\"], out var options, out _));
        Assert.Null(options.SourceName);
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Rejects_incomplete_or_unknown_arguments(string[] args)
    {
        Assert.False(ProcessOptions.TryParse(args, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
