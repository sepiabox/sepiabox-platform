using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>Where an asset's original lives, on the detail page (S-064, D-088): links, lines, and the download.</summary>
public sealed class SourceLinkTests : LocalizedContext
{
    private const string Takeout = "https://photos.google.com/photo/AF1Qip-example";

    [Fact]
    public void An_index_only_photo_links_to_google_photos_in_a_new_tab_and_has_no_download()
    {
        var page = RenderDetail(Photo(false, new AssetSourceDetail("google-takeout", "Takeout 2026", "Google Photos/IMG_0001.jpg", Takeout)));

        var link = page.Find("ul.sources a.source-link");
        Assert.Equal(Takeout, link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Equal("Open in Google Photos (opens in a new tab)", link.TextContent);
        Assert.Empty(page.FindAll("a.button"));
        Assert.DoesNotContain("Download the original", page.Markup, StringComparison.Ordinal);
        // The link says where it leads; no second line for a source that has one.
        Assert.Empty(page.FindAll(".source-line"));
    }

    [Fact]
    public void An_instagram_source_reads_open_on_instagram_and_any_other_kind_open_at_the_source()
    {
        var page = RenderDetail(Photo(
            false,
            new AssetSourceDetail("instagram", "Instagram", "posts/1.jpg", "https://www.instagram.com/p/abc/"),
            new AssetSourceDetail("some-new-export", "Other", "2.jpg", "https://example.com/2")));

        Assert.Equal(
            ["Open on Instagram (opens in a new tab)", "Open at the source (opens in a new tab)"],
            page.FindAll("ul.sources a.source-link").Select(a => a.TextContent));
    }

    [Fact]
    public void A_local_source_of_an_index_only_photo_says_which_drive_and_path_with_no_link_and_no_download()
    {
        var page = RenderDetail(Photo(false, new AssetSourceDetail("local", "Life", "2019/IMG_0001.jpg")));

        Assert.Equal("Original on the drive 'Life': 2019/IMG_0001.jpg", page.Find("ul.sources .source-line").TextContent);
        Assert.Equal("2019/IMG_0001.jpg", page.Find("ul.sources .source-line .mono").TextContent);
        Assert.Equal("Sepiabox keeps no copy of this original: it stays at its source.", page.Find("p.kept-at-source").TextContent);
        Assert.Empty(page.FindAll("ul.sources a"));
        Assert.Empty(page.FindAll("a.button"));
    }

    [Theory]
    [InlineData("apple-photos", "Apple", "Original in Apple Photos / iCloud Photos; it can't be opened from here.")]
    [InlineData("facebook", "Facebook", "Original on Facebook; it can't be opened from here.")]
    [InlineData("google-takeout", "Takeout 2026", "Original in Takeout 2026; it can't be opened from here.")]
    [InlineData("instagram", "Instagram", "Original in Instagram; it can't be opened from here.")]
    [InlineData("some-new-export", "Mum's export", "Original in Mum's export; it can't be opened from here.")]
    public void An_export_source_without_an_address_says_where_the_original_is_kept(string type, string name, string line)
    {
        var page = RenderDetail(Photo(false, new AssetSourceDetail(type, name, "x/1.jpg")));

        Assert.Equal(line, page.Find("ul.sources .source-line").TextContent);
        Assert.Empty(page.FindAll("ul.sources a"));
    }

    [Theory]
    [InlineData("google-drive")]
    [InlineData("onedrive")]
    public void A_cloud_source_keeps_its_name_and_path_for_the_stories_that_add_its_line(string type)
    {
        var page = RenderDetail(Photo(false, new AssetSourceDetail(type, "Ana's Drive", "Fotos/1.jpg")));

        Assert.Empty(page.FindAll(".source-line"));
        Assert.Equal("Ana's Drive", page.Find("ul.sources .source-name").TextContent);
        Assert.Equal("Fotos/1.jpg", page.Find("ul.sources .mono").TextContent);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("http://photos.google.com/photo/1")]
    [InlineData("ftp://photos.google.com/photo/1")]
    [InlineData("//photos.google.com/photo/1")]
    [InlineData("photos.google.com/photo/1")]
    [InlineData("/photo/1")]
    [InlineData("https:///photo/1")]
    [InlineData("https://")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_source_address_that_is_not_https_is_never_a_link(string url)
    {
        var page = RenderDetail(Photo(false, new AssetSourceDetail("google-takeout", "Takeout 2026", "a.jpg", url)));

        Assert.Empty(page.FindAll("ul.sources a"));
        Assert.DoesNotContain("javascript", page.Markup, StringComparison.OrdinalIgnoreCase);
        // With no usable address the original is still said to be kept at the source.
        Assert.Equal("Original in Takeout 2026; it can't be opened from here.", page.Find(".source-line").TextContent);
    }

    [Fact]
    public void Shows_an_https_address_as_the_browser_would_read_it()
    {
        Assert.Equal("https://photos.example.com/a%20b", SourceLinks.SafeUrl("HTTPS://photos.example.com/a b"));
        Assert.Equal("https://photos.example.com/", SourceLinks.SafeUrl("https://photos.example.com"));
        Assert.Null(SourceLinks.SafeUrl(null));
    }

    [Fact]
    public void A_stored_original_keeps_its_download_and_shows_the_sources_link_too()
    {
        var page = RenderDetail(Photo(true, new AssetSourceDetail("google-takeout", "Takeout 2026", "a.jpg", Takeout)));

        Assert.Equal("Download the original", page.Find("a.button").TextContent);
        Assert.Equal(Takeout, page.Find("ul.sources a.source-link").GetAttribute("href"));
        Assert.Equal("Takeout 2026", page.Find("ul.sources .source-name").TextContent);
    }

    [Fact]
    public void A_stored_original_with_no_address_shows_no_stored_line()
    {
        var page = RenderDetail(Photo(true, new AssetSourceDetail("local", "Life", "2019/IMG_0001.jpg")));

        Assert.Empty(page.FindAll(".source-line"));
        Assert.Equal("Life", page.Find("ul.sources .source-name").TextContent);
        Assert.NotEmpty(page.FindAll("a.button"));
        Assert.Empty(page.FindAll("p.kept-at-source"));
    }

    [Theory]
    [InlineData("proxy-1080p", "proxy-1080p")]
    [InlineData("proxy-720p", "proxy-720p")]
    [InlineData(null, "proxy-720p")]
    public void Plays_the_1080p_version_when_the_video_has_it_and_the_720p_one_otherwise(string? proxy, string played)
    {
        var page = RenderDetail(Details.Video(Ids.Of(3)) with { ProxyName = proxy });

        Assert.Equal($"photos/{Ids.Of(3)}/{played}", page.Find("figure.preview video").GetAttribute("src"));
    }

    [Fact]
    public void Shows_the_larger_preview_it_is_given()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(5), previewName: "preview-2048"));

        Assert.Equal($"photos/{Ids.Of(5)}/preview-2048", page.Find("figure.preview img").GetAttribute("src"));
    }

    [Theory]
    [InlineData("google-takeout", "Abrir no Google Fotos (abre em uma nova aba)")]
    [InlineData("instagram", "Abrir no Instagram (abre em uma nova aba)")]
    [InlineData("apple-photos", "Abrir na origem (abre em uma nova aba)")]
    public void Speaks_brazilian_portuguese_for_a_link(string type, string text)
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Photo(false, new AssetSourceDetail(type, "Fonte", "a.jpg", Takeout)));

        Assert.Equal(text, page.Find("ul.sources a.source-link").TextContent);
        Assert.Empty(page.FindAll("a.button"));
    }

    [Theory]
    [InlineData("local", "Life", "Original no HD 'Life': 2019/IMG_0001.jpg")]
    [InlineData("apple-photos", "Apple", "Original no Fotos da Apple / Fotos do iCloud; não dá para abrir daqui.")]
    [InlineData("facebook", "Facebook", "Original no Facebook; não dá para abrir daqui.")]
    [InlineData("google-takeout", "Takeout 2026", "Original em Takeout 2026; não dá para abrir daqui.")]
    public void Speaks_brazilian_portuguese_for_a_line(string type, string name, string line)
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Photo(false, new AssetSourceDetail(type, name, "2019/IMG_0001.jpg")));

        Assert.Equal(line, page.Find("ul.sources .source-line").TextContent);
        Assert.Equal("O Sepiabox não guarda uma cópia deste original: ele fica na origem.", page.Find("p.kept-at-source").TextContent);
    }

    private static AssetDetail Photo(bool stored, params AssetSourceDetail[] sources) =>
        Details.Photo(Ids.Of(1), new DateTime(2019, 7, 14, 10, 32, 0)) with { Sources = sources, OriginalStored = stored };

    private IRenderedComponent<PhotoDetail> RenderDetail(AssetDetail asset)
    {
        var library = new FakeLibrary();
        library.Assets[asset.Id] = asset;
        Services.AddSingleton<ILibraryQueries>(library);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}");
        return Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));
    }
}
