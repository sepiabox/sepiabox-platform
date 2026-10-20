using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class PhotoDetailTests : BunitContext
{
    [Fact]
    public void A_photo_shows_its_preview_details_sources_and_download_link()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1), new DateTime(2019, 7, 14, 10, 32, 0)));

        Assert.Equal($"fotos/{Ids.Of(1)}/preview-1280", page.Find("figure.preview img").GetAttribute("src"));
        Assert.Equal("14 de julho de 2019, 10:32", page.Find("h1").TextContent);
        Assert.Contains("Apple iPhone 15", page.Markup, StringComparison.Ordinal);
        Assert.Contains("HD Ana", page.Find("ul.sources").TextContent, StringComparison.Ordinal);
        Assert.Equal($"fotos/{Ids.Of(1)}/original", page.Find("a.button").GetAttribute("href"));
        // Enhanced navigation would fetch the redirect itself, failing on CORS and reading the original twice.
        Assert.Equal("false", page.Find("a.button").GetAttribute("data-enhance-nav"));
        // No `download`: the file name comes from Content-Disposition, and a failure page must stay visible.
        Assert.False(page.Find("a.button").HasAttribute("download"));
        Assert.DoesNotContain("sig=", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_video_with_its_720p_version_is_played_with_its_poster()
    {
        var page = RenderDetail(Details.Video(Ids.Of(2)));

        var video = page.Find("figure.preview video");
        Assert.Equal($"fotos/{Ids.Of(2)}/proxy-720p", video.GetAttribute("src"));
        Assert.Equal($"fotos/{Ids.Of(2)}/poster", video.GetAttribute("poster"));
        Assert.Equal("Vídeo de 9 de agosto de 2020, 18:05", video.GetAttribute("aria-label"));
        Assert.Contains("Vídeo", page.Find("dl").TextContent, StringComparison.Ordinal);
        Assert.Contains("0:03", page.Find("dl").TextContent, StringComparison.Ordinal);
        Assert.Equal($"fotos/{Ids.Of(2)}/original", page.Find("a.button").GetAttribute("href"));
        Assert.DoesNotContain("sig=", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_video_without_its_720p_version_says_it_is_still_processing()
    {
        var page = RenderDetail(Details.Video(Ids.Of(6), hasProxy: false));

        Assert.Empty(page.FindAll("video"));
        Assert.Equal($"fotos/{Ids.Of(6)}/poster", page.Find("figure.preview img").GetAttribute("src"));
        Assert.Equal("Vídeo ainda em processamento", page.Find("figure.preview .processing").TextContent);
    }

    [Fact]
    public void A_photo_has_no_player_or_length()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(7)) with { DurationSeconds = 3, HasProxy = true });

        Assert.Empty(page.FindAll("video"));
        Assert.DoesNotContain("Duração", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_asset_without_derivatives_says_there_is_no_preview()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(3), new DateTime(2020, 1, 1), previewName: null));

        Assert.Empty(page.FindAll("figure.preview img"));
        Assert.Equal("Sem pré-visualização", page.Find("figure.preview .placeholder").TextContent);
    }

    [Fact]
    public void An_undated_asset_is_titled_sem_data()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(4), capturedAt: null));

        Assert.Equal("Sem data", page.Find("h1").TextContent);
    }

    [Fact]
    public void The_back_link_returns_to_the_timeline_page_the_visitor_came_from()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(5), new DateTime(2020, 1, 1)), pagina: 3);

        Assert.Equal("./?pagina=3", page.Find("p.back a").GetAttribute("href"));
    }

    private IRenderedComponent<PhotoDetail> RenderDetail(AssetDetail asset, int? pagina = null)
    {
        var library = new FakeLibrary();
        library.Assets[asset.Id] = asset;
        Services.AddSingleton<ILibraryQueries>(library);
        var query = pagina is { } page ? $"?pagina={page}" : string.Empty;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/fotos/{asset.Id}{query}");
        return Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));
    }
}
