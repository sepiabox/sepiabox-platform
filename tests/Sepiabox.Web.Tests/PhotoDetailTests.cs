using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class PhotoDetailTests : LocalizedContext
{
    [Fact]
    public void A_photo_shows_its_preview_details_sources_and_download_link()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1), new DateTime(2019, 7, 14, 10, 32, 0)));

        Assert.Equal($"photos/{Ids.Of(1)}/preview-1280", page.Find("figure.preview img").GetAttribute("src"));
        Assert.Equal("July 14, 2019, 10:32", page.Find("h1").TextContent);
        Assert.Contains("Apple iPhone 15", page.Markup, StringComparison.Ordinal);
        Assert.Contains("HD Ana", page.Find("ul.sources").TextContent, StringComparison.Ordinal);
        Assert.Equal($"photos/{Ids.Of(1)}/original", page.Find("a.button").GetAttribute("href"));
        Assert.Equal("Download the original", page.Find("a.button").TextContent);
        Assert.Contains("Camera", page.Find("dl").TextContent, StringComparison.Ordinal);
        Assert.Equal("← Timeline", page.Find("p.back a").TextContent);
        // Enhanced navigation would fetch the redirect itself, failing on CORS and reading the original twice.
        Assert.Equal("false", page.Find("a.button").GetAttribute("data-enhance-nav"));
        // No `download`: the file name comes from Content-Disposition, and a failure page must stay visible.
        Assert.False(page.Find("a.button").HasAttribute("download"));
        Assert.DoesNotContain("sig=", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_photo_with_a_place_shows_its_city_region_and_country()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Place = "Matosinhos, Porto, Portugal" });

        Assert.Equal("Place", page.Find("dl dt").TextContent);
        Assert.Equal("Matosinhos, Porto, Portugal", page.Find("dl dd").TextContent);
    }

    [Fact]
    public void A_photo_without_a_place_shows_no_place()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)));

        Assert.DoesNotContain("Place", page.Find("dl").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_video_with_its_720p_version_is_played_with_its_poster()
    {
        var page = RenderDetail(Details.Video(Ids.Of(2)));

        var video = page.Find("figure.preview video");
        Assert.Equal($"photos/{Ids.Of(2)}/proxy-720p", video.GetAttribute("src"));
        Assert.Equal($"photos/{Ids.Of(2)}/poster", video.GetAttribute("poster"));
        Assert.Equal("Video from August 9, 2020, 18:05", video.GetAttribute("aria-label"));
        Assert.Contains("Video", page.Find("dl").TextContent, StringComparison.Ordinal);
        Assert.Contains("0:03", page.Find("dl").TextContent, StringComparison.Ordinal);
        Assert.Equal($"photos/{Ids.Of(2)}/original", page.Find("a.button").GetAttribute("href"));
        Assert.DoesNotContain("sig=", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_video_without_its_720p_version_says_it_is_still_processing()
    {
        var page = RenderDetail(Details.Video(Ids.Of(6), hasProxy: false));

        Assert.Empty(page.FindAll("video"));
        Assert.Equal($"photos/{Ids.Of(6)}/poster", page.Find("figure.preview img").GetAttribute("src"));
        Assert.Equal("Video still processing", page.Find("figure.preview .processing").TextContent);
    }

    [Fact]
    public void A_photo_has_no_player_or_length()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(7)) with { DurationSeconds = 3, HasProxy = true });

        Assert.Empty(page.FindAll("video"));
        Assert.DoesNotContain("Duration", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_asset_without_derivatives_says_there_is_no_preview()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(3), new DateTime(2020, 1, 1), previewName: null));

        Assert.Empty(page.FindAll("figure.preview img"));
        Assert.Equal("No preview", page.Find("figure.preview .placeholder").TextContent);
    }

    [Fact]
    public void An_undated_asset_is_titled_no_date()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(4), capturedAt: null));

        Assert.Equal("No date", page.Find("h1").TextContent);
    }

    [Fact]
    public void A_photo_is_described_in_brazilian_portuguese_when_the_culture_is_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Details.Photo(Ids.Of(8), new DateTime(2019, 7, 14, 10, 32, 0)));

        Assert.Equal("14 de julho de 2019, 10:32", page.Find("h1").TextContent);
        Assert.Equal("Foto", page.Find("figure.preview img").GetAttribute("alt"));
        Assert.Equal("Baixar o original", page.Find("a.button").TextContent);
        Assert.Equal("← Linha do tempo", page.Find("p.back a").TextContent);
        var facts = page.Find("dl").TextContent;
        Assert.Contains("Câmera", facts, StringComparison.Ordinal);
        Assert.Contains("Dimensões", facts, StringComparison.Ordinal);
        Assert.Contains("Tipo", facts, StringComparison.Ordinal);
        Assert.Equal("Onde está", page.Find("h2").TextContent);
    }

    [Fact]
    public void A_video_is_labelled_in_brazilian_portuguese_when_the_culture_is_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Details.Video(Ids.Of(9)));

        Assert.Equal("Vídeo de 9 de agosto de 2020, 18:05", page.Find("video").GetAttribute("aria-label"));
        Assert.Contains("Duração", page.Find("dl").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_video_without_its_720p_version_says_it_is_still_processing_in_brazilian_portuguese()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Details.Video(Ids.Of(10), hasProxy: false));

        Assert.Equal("Vídeo ainda em processamento", page.Find("figure.preview .processing").TextContent);
    }

    [Fact]
    public void An_undated_asset_without_a_preview_is_described_in_brazilian_portuguese()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Details.Photo(Ids.Of(11), capturedAt: null, previewName: null));

        Assert.Equal("Sem data", page.Find("h1").TextContent);
        Assert.Equal("Sem prévia", page.Find("figure.preview .placeholder").TextContent);
    }

    [Fact]
    public void The_back_link_returns_to_the_timeline_page_the_visitor_came_from()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(5), new DateTime(2020, 1, 1)), pageNumber: 3);

        Assert.Equal("./?page=3", page.Find("p.back a").GetAttribute("href"));
    }

    private IRenderedComponent<PhotoDetail> RenderDetail(AssetDetail asset, int? pageNumber = null)
    {
        var library = new FakeLibrary();
        library.Assets[asset.Id] = asset;
        Services.AddSingleton<ILibraryQueries>(library);
        var query = pageNumber is { } number ? $"?page={number}" : string.Empty;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}{query}");
        return Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));
    }
}
