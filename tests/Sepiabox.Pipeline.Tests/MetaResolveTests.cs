using Sepiabox.Pipeline.Exports;

namespace Sepiabox.Pipeline.Tests;

/// <summary>
/// Where a media <c>uri</c> of a Meta download points (S-048, S-049, D-111): inside the download only, whatever the list
/// says. The files are real, including some outside the download, which is the only way to see the escape check work.
/// </summary>
public sealed class MetaResolveTests : IDisposable
{
    private readonly TempFolder _outer = new();
    private readonly string _root;

    public MetaResolveTests()
    {
        _root = Path.Combine(_outer.Path, "download");
        Directory.CreateDirectory(_root);
        _outer.Write("sibling/x.jpg", [1]);
        _outer.Write("secret.jpg", [1]);
        _outer.Write("download/media/a.jpg", [1]);
        _outer.Write("download/media/notes.txt", [1]);
        _outer.Write("download/café.jpg", [1]);
        _outer.Write("download/cafÃ© raw.jpg", [1]);
    }

    public void Dispose() => _outer.Dispose();

    [Theory]
    [InlineData("../sibling/x.jpg")]
    [InlineData("../secret.jpg")]
    [InlineData("media/../../sibling/x.jpg")]
    [InlineData("..\\sibling\\x.jpg")]
    [InlineData("/../sibling/x.jpg")]
    [InlineData("media/../../../secret.jpg")]
    [InlineData("..")]
    public void Never_resolves_a_path_that_leaves_the_download_even_when_the_file_exists(string uri)
    {
        Assert.Null(MetaExport.Resolve(_root, string.Empty, Media(uri)));
    }

    [Fact]
    public void Never_resolves_a_rooted_path_to_a_file_outside_the_download()
    {
        var absolute = Path.Combine(_outer.Path, "sibling", "x.jpg");

        Assert.True(File.Exists(absolute));
        Assert.Null(MetaExport.Resolve(_root, string.Empty, Media(absolute)));
        Assert.Null(MetaExport.Resolve(_root, string.Empty, Media("/etc/x.jpg")));
    }

    [Fact]
    public void Skips_a_uri_with_a_nul_instead_of_throwing()
    {
        Assert.Null(MetaExport.Resolve(_root, string.Empty, Media("media/a\0b.jpg")));
        Assert.Equal("media/a.jpg", MetaExport.Resolve(_root, string.Empty, Media("media/a.jpg")));
    }

    [Fact]
    public void Treats_a_leading_slash_and_backslashes_as_a_path_inside_the_download()
    {
        Assert.Equal("media/a.jpg", MetaExport.Resolve(_root, string.Empty, Media("/media/a.jpg")));
        Assert.Equal("media/a.jpg", MetaExport.Resolve(_root, string.Empty, Media("media\\a.jpg")));
    }

    [Fact]
    public void Resolves_under_the_base_folder_of_the_download()
    {
        Assert.Equal("download/media/a.jpg", MetaExport.Resolve(_outer.Path, "download/", Media("media/a.jpg")));
    }

    [Fact]
    public void Finds_a_file_by_its_name_as_written_or_repaired_whichever_is_on_disk()
    {
        var repairedOnly = new MetaMedia("café.jpg", "cafÃ©.jpg", null, null, null);
        var rawOnly = new MetaMedia("café raw.jpg", "cafÃ© raw.jpg", null, null, null);

        Assert.Equal("café.jpg", MetaExport.Resolve(_root, string.Empty, repairedOnly));
        Assert.Equal("cafÃ© raw.jpg", MetaExport.Resolve(_root, string.Empty, rawOnly));
    }

    [Fact]
    public void Does_not_resolve_an_existing_file_that_is_not_a_photo_or_a_video_or_a_missing_one()
    {
        Assert.Null(MetaExport.Resolve(_root, string.Empty, Media("media/notes.txt")));
        Assert.Null(MetaExport.Resolve(_root, string.Empty, Media("media/gone.jpg")));
    }

    private static MetaMedia Media(string uri) => new(uri, uri, null, null, null);
}
