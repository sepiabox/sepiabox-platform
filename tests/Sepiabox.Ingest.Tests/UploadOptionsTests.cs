using Sepiabox.Ingest.Uploading;

namespace Sepiabox.Ingest.Tests;

public class UploadOptionsTests
{
    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "work" },
        new[] { "--destination", "local" },
        new[] { "work", "--destination" },
        new[] { "work", "--destination", "s3" },
        new[] { "work", "--destination", "azure" },
        new[] { "work", "--destination", "azure", "--account", " " },
        new[] { "work", "other", "--destination", "local" },
        new[] { "work", "--destination", "local", "--fast" },
    };

    [Fact]
    public void Parses_a_local_upload()
    {
        Assert.True(UploadOptions.TryParse(["work", "--destination", "local"], out var options, out _));

        Assert.Equal(new UploadOptions("work", UploadDestination.Local), options);
    }

    [Fact]
    public void Parses_an_azure_upload_with_its_account()
    {
        Assert.True(UploadOptions.TryParse(["--destination", "azure", "--account", "sepiaboxdata", "work"], out var options, out _));

        Assert.Equal(new UploadOptions("work", UploadDestination.Azure, "sepiaboxdata"), options);
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Rejects_incomplete_or_unknown_arguments(string[] args)
    {
        Assert.False(UploadOptions.TryParse(args, out var options, out var error));

        Assert.Null(options);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
