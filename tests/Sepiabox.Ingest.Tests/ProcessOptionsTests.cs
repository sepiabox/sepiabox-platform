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
    };

    [Fact]
    public void Reads_the_ffmpeg_folder_and_the_nvenc_encoder()
    {
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work", "--ffmpeg", "C:\\ffmpeg\\bin", "--hw", "nvenc"], out var options, out _));
        Assert.Equal("C:\\ffmpeg\\bin", options.FfmpegFolder);
        Assert.Equal(VideoEncoder.Nvenc, options.Encoder);
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
