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

    [Fact]
    public void Parses_resend_source_and_refuses_it_without_a_name()
    {
        Assert.True(UploadOptions.TryParse(["work", "--destination", "local", "--resend-source", " wrong-drive "], out var options, out _));
        Assert.Equal(new UploadOptions("work", UploadDestination.Local, ResendSource: "wrong-drive"), options);
        Assert.False(UploadOptions.TryParse(["work", "--destination", "local", "--resend-source"], out _, out var missing));
        Assert.Equal("--resend-source needs a value.", missing);
        Assert.False(UploadOptions.TryParse(["work", "--destination", "local", "--resend-source", " "], out _, out var blank));
        Assert.Equal("--resend-source needs the name of a source.", blank);
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
