using Sepiabox.Ingest.Models;

namespace Sepiabox.Ingest.Tests;

public class ModelsOptionsTests
{
    [Theory]
    [InlineData(new string[0], "Tell models what to do: models download.")]
    [InlineData(new[] { "fetch" }, "Unknown models command 'fetch'. The only one is models download.")]
    [InlineData(new[] { "download", "--models" }, "--models needs a value.")]
    [InlineData(new[] { "download", "--gpu" }, "Unknown option '--gpu' for models download.")]
    [InlineData(new[] { "download", "--only" }, "--only needs model names separated by commas.")]
    [InlineData(new[] { "download", "--only", " , " }, "--only needs at least one model name.")]
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
    public void Reads_the_models_to_download_when_only_some_are_wanted()
    {
        Assert.True(ModelsOptions.TryParse(["download", "--only", "clip-vit-b32-text, clip-bpe-vocab"], out var options, out _));
        Assert.Equal(["clip-vit-b32-text", "clip-bpe-vocab"], options.Only!);
        Assert.False(ModelsOptions.TryParse(["download", "--only", "clip-vit-b32-text,gpt"], out _, out var error));
        Assert.StartsWith("Unknown model 'gpt' for --only", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Uses_the_default_folder_without_one()
    {
        Assert.True(ModelsOptions.TryParse(["download"], out var options, out _));
        Assert.Null(options.Folder);
    }
}
