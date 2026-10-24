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

    [Fact]
    public void The_back_link_returns_to_the_filters_the_visitor_came_from()
    {
        var filters = SearchLinks.Timeline(SearchLinks.FromFields("2019", "12", null, "Lisbon", null));

        var page = RenderDetail(Details.Photo(Ids.Of(5), new DateTime(2019, 12, 1)), query: filters[2..]);

        Assert.Equal(filters, page.Find("p.back a").GetAttribute("href"));
    }

    [Fact]
    public void The_back_link_ignores_filters_that_do_not_decode()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(5), new DateTime(2019, 12, 1)), query: "?q=%3Cscript%3E");

        Assert.Equal("./", page.Find("p.back a").GetAttribute("href"));
    }

    [Fact]
    public void Links_to_the_photos_similar_to_it_when_it_has_an_embedding()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(5), new DateTime(2019, 12, 1)) with { HasEmbedding = true });

        var link = page.Find("p.similar a");
        Assert.Equal("Similar", link.TextContent);
        Assert.Equal(SearchLinks.Timeline(SearchLinks.Similar(Ids.Of(5))), link.GetAttribute("href"));
        Assert.DoesNotContain("not been analysed", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_the_photo_has_not_been_analysed_yet_when_it_has_no_embedding()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(5), new DateTime(2019, 12, 1)));

        Assert.Equal("This photo has not been analysed yet.", page.Find("p.similar[role=status]").TextContent);
        Assert.Empty(page.FindAll("p.similar a"));
    }

    [Theory]
    [InlineData(Cultures.English, "This video has not been analysed yet.")]
    [InlineData(Cultures.BrazilianPortuguese, "Este vídeo ainda não foi analisado.")]
    public void Says_the_video_has_not_been_analysed_yet_when_a_video_has_no_embedding(string culture, string expected)
    {
        using var language = Cultures.Use(culture);

        var page = RenderDetail(Details.Video(Ids.Of(2)));

        Assert.Equal(expected, page.Find("p.similar").TextContent);
    }

    [Fact]
    public void Says_the_photo_has_not_been_analysed_yet_in_brazilian_portuguese_when_it_has_no_embedding()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Details.Photo(Ids.Of(5), new DateTime(2019, 12, 1)));

        Assert.Equal("Esta foto ainda não foi analisada.", page.Find("p.similar").TextContent);
    }

    [Fact]
    public void Names_the_link_parecidas_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Details.Photo(Ids.Of(5), new DateTime(2019, 12, 1)) with { HasEmbedding = true });

        Assert.Equal("Parecidas", page.Find("p.similar a").TextContent);
    }

    [Fact]
    public void Shows_the_caption_as_written_and_each_album_linking_to_the_albums_filter()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Caption = "Praia <b>de</b> Matosinhos\nCom a avó", Albums = ["Verão 2019", "Wedding"] });

        var caption = page.Find("dd.caption");
        Assert.Equal("Praia <b>de</b> Matosinhos\nCom a avó", caption.TextContent);
        Assert.Empty(caption.QuerySelectorAll("b"));
        Assert.Equal("Caption", Assert.Single(page.FindAll("dl dt"), d => d.TextContent == "Caption").TextContent);
        Assert.Contains("Albums", page.FindAll("h2").Select(h => h.TextContent));
        var links = page.FindAll("ul.photo-albums a");
        Assert.Equal(["Verão 2019", "Wedding"], links.Select(l => l.TextContent));
        var first = SearchLinks.Timeline(new Sepiabox.Catalog.Search.SearchQuery { Filters = new Sepiabox.Catalog.Search.SearchFilters { Albums = ["Verão 2019"] } });
        Assert.Equal(first, links[0].GetAttribute("href"));
        Assert.StartsWith("./?q=", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_the_caption_and_albums_with_brazilian_portuguese_labels()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Caption = "Férias na praia", Albums = ["Verão 2019"] });

        Assert.Contains(page.FindAll("dl dt"), d => d.TextContent == "Legenda");
        Assert.Equal("Férias na praia", page.Find("dd.caption").TextContent);
        Assert.Contains("Álbuns", page.FindAll("h2").Select(h => h.TextContent));
        Assert.Equal("Verão 2019", page.Find("ul.photo-albums a").TextContent);
    }

    [Fact]
    public void Shows_the_first_five_albums_and_folds_the_rest_away()
    {
        var albums = Enumerable.Range(1, 12).Select(n => $"Album {n:00}").ToList();

        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Albums = albums });

        Assert.Equal(albums.Take(5), page.FindAll("section.facts > ul.photo-albums a").Select(a => a.TextContent));
        Assert.Equal("7 more albums", page.Find("details.more-albums summary").TextContent);
        Assert.Equal(albums.Skip(5), page.FindAll("details.more-albums a").Select(a => a.TextContent));
    }

    [Fact]
    public void Says_one_more_album_in_the_singular_in_both_languages()
    {
        var albums = Enumerable.Range(1, 6).Select(n => $"Album {n}").ToList();
        Assert.Equal("1 more album", RenderDetail(Details.Photo(Ids.Of(1)) with { Albums = albums }).Find("details.more-albums summary").TextContent);
    }

    [Fact]
    public void Says_one_more_album_in_the_singular_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        var albums = Enumerable.Range(1, 6).Select(n => $"Álbum {n}").ToList();

        Assert.Equal("Mais 1 álbum", RenderDetail(Details.Photo(Ids.Of(1)) with { Albums = albums }).Find("details.more-albums summary").TextContent);
    }

    [Fact]
    public void Folds_nothing_away_for_five_albums()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(2)) with { Albums = [.. Enumerable.Range(1, 5).Select(n => $"Album {n}")] });

        Assert.Equal(5, page.FindAll("ul.photo-albums a").Count);
        Assert.Empty(page.FindAll("details.more-albums"));
    }

    [Fact]
    public void Says_the_date_is_from_the_export_for_an_export_date()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1), new DateTime(2019, 7, 14)) with { CapturedAtSource = "export" });

        Assert.Equal("Export", page.FindAll("dl dt").Single(d => d.TextContent == "Date source").NextElementSibling!.TextContent);
    }

    [Fact]
    public void Says_nothing_about_the_date_s_source_when_it_is_the_cameras()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(2), new DateTime(2019, 7, 14)) with { CapturedAtSource = "metadata" });

        Assert.DoesNotContain(page.FindAll("dl dt"), d => d.TextContent == "Date source");
    }

    [Fact]
    public void Says_the_date_is_from_the_export_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Details.Photo(Ids.Of(3), new DateTime(2019, 7, 14)) with { CapturedAtSource = "export" });

        Assert.Equal("Exportação", page.FindAll("dl dt").Single(d => d.TextContent == "Origem da data").NextElementSibling!.TextContent);
    }

    [Fact]
    public void Shows_no_caption_and_no_albums_for_a_photo_without_them()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)));

        Assert.Empty(page.FindAll("dd.caption"));
        Assert.Empty(page.FindAll("ul.photo-albums"));
        Assert.DoesNotContain("Albums", page.FindAll("h2").Select(h => h.TextContent));
    }

    private IRenderedComponent<PhotoDetail> RenderDetail(AssetDetail asset, int? pageNumber = null, string? query = null)
    {
        var library = new FakeLibrary();
        library.Assets[asset.Id] = asset;
        Services.AddSingleton<ILibraryQueries>(library);
        query ??= pageNumber is { } number ? $"?page={number}" : string.Empty;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}{query}");
        return Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));
    }
}
