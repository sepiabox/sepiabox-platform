using Sepiabox.Ingest.Processing;

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
    };

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
