using Sepiabox.Ingest.Models;

namespace Sepiabox.Ingest.Tests;

public class ModelsOptionsTests
{
    [Theory]
    [InlineData(new string[0], "Tell models what to do: models download.")]
    [InlineData(new[] { "fetch" }, "Unknown models command 'fetch'. The only one is models download.")]
    [InlineData(new[] { "download", "--models" }, "--models needs a value.")]
    [InlineData(new[] { "download", "--gpu" }, "Unknown option '--gpu' for models download.")]
    public void Explains_what_is_wrong(string[] args, string expected)
    {
        Assert.False(ModelsOptions.TryParse(args, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Reads_the_models_folder()
    {
        Assert.True(ModelsOptions.TryParse(["download", "--models", "D:\\models"], out var options, out _));
        Assert.Equal("D:\\models", options.Folder);
    }

    [Fact]
    public void Uses_the_default_folder_without_one()
    {
        Assert.True(ModelsOptions.TryParse(["download"], out var options, out _));
        Assert.Null(options.Folder);
    }
}
