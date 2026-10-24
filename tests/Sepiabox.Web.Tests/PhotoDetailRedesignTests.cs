using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The photo detail and the grid as redesigned (S-068, D-124): face thumbnails, the stored-on card, the arrows and the tiles.</summary>
public sealed class PhotoDetailRedesignTests : LocalizedContext
{
    private static readonly FaceCrop s_crop = new(Ids.Of(1), [0.2, 0.2, 0.2, 0.2], 256, 192);

    [Fact]
    public void Shows_a_thumbnail_for_each_face_two_named_and_six_unnamed_make_eight_and_no_unnamed_list()
    {
        var faces = new List<AssetFace> { new(1, 3, "Ana", s_crop), new(2, 4, "Bruno", null) };
        faces.AddRange(Enumerable.Range(10, 6).Select(n => new AssetFace(n, 100 + n, null, s_crop)));

        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Faces = faces });

        var tiles = page.FindAll(".people-card .faces a.face");
        Assert.Equal(8, tiles.Count);
        Assert.Equal(2, tiles.Count(t => t.ClassList.Contains("named")));
        Assert.Equal(6, tiles.Count(t => t.ClassList.Contains("unnamed")));
        Assert.Equal(["Ana", "Bruno"], tiles.Where(t => t.ClassList.Contains("named")).Select(t => t.QuerySelector(".name")!.TextContent));
        Assert.All(tiles.Where(t => t.ClassList.Contains("unnamed")), t => Assert.Equal("Add a name", t.QuerySelector(".name")!.TextContent));
        // No list of "Unnamed" links any more: the faces are the list.
        Assert.Empty(page.FindAll(".photo-people"));
        Assert.DoesNotContain("Unnamed", page.Find(".people-card").TextContent, StringComparison.Ordinal);
        // A face with a crop shows the face; one without, the initials.
        Assert.NotNull(tiles[0].QuerySelector("img.avatar-photo"));
        Assert.Equal("B", tiles[1].QuerySelector(".avatar")!.TextContent.Trim());
    }

    [Theory]
    [InlineData(Cultures.English, "Add a name, unnamed face {0}")]
    [InlineData(Cultures.BrazilianPortuguese, "Dar um nome, rosto sem nome {0}")]
    public void Gives_each_unnamed_face_its_own_accessible_name_numbered_in_display_order_with_the_visible_text_first(string culture, string pattern)
    {
        using var language = Cultures.Use(culture);
        var faces = new[] { new AssetFace(1, 3, "Ana", null), new AssetFace(2, 8, null, null), new AssetFace(3, 9, null, null), new AssetFace(4, 10, null, null) };

        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Faces = faces });

        var tiles = page.FindAll(".faces a.face");
        Assert.Null(tiles[0].GetAttribute("aria-label"));
        var names = tiles.Skip(1).Select(t => t.GetAttribute("aria-label")).ToList();
        Assert.Equal([string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, 1), string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, 2), string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, 3)], names);
        Assert.Equal(3, names.Distinct().Count());
        // The visible text is what the name starts with (WCAG 2.5.3).
        Assert.All(tiles.Skip(1), t => Assert.StartsWith(t.QuerySelector(".name")!.TextContent, t.GetAttribute("aria-label"), StringComparison.Ordinal));
    }

    [Fact]
    public void Links_a_named_face_to_their_photos_and_an_unnamed_one_to_their_page_and_reaches_each_by_keyboard()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Faces = [new AssetFace(1, 3, "Ana", null), new AssetFace(2, 9, null, null)] });

        var links = page.FindAll(".faces a.face");

        var ana = SearchLinks.Timeline(new SearchQuery { Filters = new SearchFilters { People = new PeopleFilter { All = ["Ana"] } } });
        Assert.Equal([ana, "people/9"], links.Select(a => a.GetAttribute("href")));
        Assert.All(links, a => Assert.Null(a.GetAttribute("tabindex")));
    }

    [Fact]
    public void Offers_no_people_card_for_a_photo_with_no_counted_face()
    {
        Assert.Empty(RenderDetail(Details.Photo(Ids.Of(1))).FindAll(".people-card"));
    }

    [Fact]
    public void Cuts_a_long_path_short_and_keeps_the_whole_of_it_under_show_full_path_and_a_short_one_needs_neither()
    {
        var longPath = "2019-07-14/DCIM/100EXAMPLE/vacation-with-a-very-long-folder-name/IMG_0423.JPG";
        var asset = Details.Photo(Ids.Of(1)) with
        {
            Sources = [new AssetSourceDetail("local", "Example drive", longPath), new AssetSourceDetail("local", "Phone", "a/b.jpg")],
        };

        var page = RenderDetail(asset);

        var cards = page.FindAll("section.source-card li.src");
        Assert.Equal(2, cards.Count);
        Assert.Equal(longPath, cards[0].QuerySelector(".path")!.GetAttribute("title"));
        Assert.Equal("Show full path", cards[0].QuerySelector("details.full-path summary")!.TextContent);
        Assert.Equal(longPath, cards[0].QuerySelector("details.full-path .path-full")!.TextContent);
        Assert.Null(cards[1].QuerySelector("details.full-path"));
    }

    [Fact]
    public void Says_stored_on_in_the_card_heading_and_show_full_path_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        var asset = Details.Photo(Ids.Of(1)) with { Sources = [new AssetSourceDetail("local", "HD", new string('x', 60) + ".jpg")] };

        var page = RenderDetail(asset);

        Assert.Equal("Onde está", page.Find("section.source-card h2").TextContent);
        Assert.Equal("Mostrar o caminho completo", page.Find("details.full-path summary").TextContent);
        Assert.Equal("Detalhes desta foto", page.Find("aside.side").GetAttribute("aria-label"));
    }

    private static TimelineItem[] Five() =>
        [.. Enumerable.Range(1, 5).Select(n => new TimelineItem(Ids.Of(n), AssetKinds.Photo, new DateTime(2019, 7, 20 - n), true, null))];

    [Fact]
    public void Links_the_photos_before_and_after_in_the_timeline_page()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(3)), new FakeLibrary(Five()), address: "?page=1");

        Assert.Equal($"photos/{Ids.Of(2)}?page=1", page.Find("a.nav-arrow.prev").GetAttribute("href"));
        Assert.Equal($"photos/{Ids.Of(4)}?page=1", page.Find("a.nav-arrow.next").GetAttribute("href"));
        Assert.Equal(["Previous photo", "Next photo"], page.FindAll("a.nav-arrow").Select(a => a.GetAttribute("aria-label")));
    }

    [Fact]
    public void Has_no_previous_link_for_the_first_photo_of_the_page()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)), new FakeLibrary(Five()), address: "?page=1");

        Assert.Empty(page.FindAll("a.nav-arrow.prev"));
        Assert.Single(page.FindAll("a.nav-arrow.next"));
    }

    [Fact]
    public void Looks_for_no_neighbours_and_shows_no_arrows_for_a_visitor_who_came_from_no_listing()
    {
        // From a person's page, an occasion's link or a shared address: no page and no filters in the address.
        var library = new FakeLibrary(Five());

        var page = RenderDetail(Details.Photo(Ids.Of(3)), library);

        Assert.Empty(page.FindAll("a.nav-arrow"));
        Assert.Null(library.RequestedPage);
        Assert.Empty(library.Searches);
    }

    [Fact]
    public void Shows_the_photo_without_arrows_and_logs_it_when_the_listing_cannot_be_read()
    {
        var log = new ListLogger<PhotoDetail>();
        Services.AddSingleton<ILogger<PhotoDetail>>(log);
        var library = new FakeLibrary(Five()) { TimelineFailure = new InvalidOperationException("the timeline is down") };

        var page = RenderDetail(Details.Photo(Ids.Of(3)), library, address: "?page=2");

        Assert.Empty(page.FindAll("a.nav-arrow"));
        Assert.NotEmpty(page.FindAll("figure.preview img"));
        Assert.Contains(log.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("could not be found", StringComparison.Ordinal));
    }

    [Fact]
    public void Shows_the_photo_without_arrows_when_the_search_cannot_be_read()
    {
        var encoded = SearchQueryJson.Encode(SearchLinks.FromFields(null, "12", null, null, null));
        var library = new FakeLibrary { SearchFailure = new InvalidOperationException("search is down") };

        var page = RenderDetail(Details.Photo(Ids.Of(2)), library, address: $"?{SearchLinks.QueryParameter}={Uri.EscapeDataString(encoded)}");

        Assert.Empty(page.FindAll("a.nav-arrow"));
        Assert.NotNull(page.Find("h1"));
    }

    [Fact]
    public void Carries_the_filters_to_the_arrows_so_the_order_and_the_way_back_stay()
    {
        var items = new[] { 1, 2, 3 }.Select(n => new TimelineItem(Ids.Of(n), AssetKinds.Photo, new DateTime(2019, 12, 30 - n), true, null)).ToArray();
        var encoded = SearchQueryJson.Encode(SearchLinks.FromFields(null, "12", null, null, null));

        var page = RenderDetail(Details.Photo(Ids.Of(2)), new FakeLibrary { SearchResults = new SearchPage(items, false) }, address: $"?{SearchLinks.QueryParameter}={Uri.EscapeDataString(encoded)}");

        Assert.Contains($"photos/{Ids.Of(1)}?{SearchLinks.QueryParameter}=", page.Find("a.nav-arrow.prev").GetAttribute("href"), StringComparison.Ordinal);
        Assert.Contains($"photos/{Ids.Of(3)}?{SearchLinks.QueryParameter}=", page.Find("a.nav-arrow.next").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public void Has_no_arrows_for_a_ranking_such_as_similar_photos_whose_order_is_not_a_listing()
    {
        var items = new[] { 1, 2, 3 }.Select(n => new TimelineItem(Ids.Of(n), AssetKinds.Photo, new DateTime(2019, 12, 30 - n), true, null)).ToArray();
        var encoded = SearchQueryJson.Encode(SearchLinks.Similar(Ids.Of(2)));

        var page = RenderDetail(Details.Photo(Ids.Of(2)), new FakeLibrary { SearchResults = new SearchPage(items, false) }, address: $"?{SearchLinks.QueryParameter}={Uri.EscapeDataString(encoded)}");

        Assert.Empty(page.FindAll("a.nav-arrow"));
    }

    [Fact]
    public void Puts_the_download_and_find_similar_actions_in_the_facts_card_and_keeps_the_download_out_of_enhanced_navigation()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { HasEmbedding = true });

        var actions = page.FindAll("section.facts a.button");
        Assert.Equal(2, actions.Count);
        Assert.Equal("false", actions[1].GetAttribute("data-enhance-nav"));
        Assert.Contains("secondary", actions[0].ClassList);
    }

    private IRenderedComponent<PhotoDetail> RenderDetail(AssetDetail asset, FakeLibrary? library = null, string address = "")
    {
        library ??= new FakeLibrary();
        library.Assets[asset.Id] = asset;
        Services.AddSingleton<ILibraryQueries>(library);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}{address}");
        return Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));
    }
}

/// <summary>The grid's tiles and month headings as redesigned (S-068).</summary>
public sealed class GridRedesignTests : LocalizedContext
{
    [Fact]
    public void Shows_the_date_over_a_tile_as_a_decoration_and_keeps_the_link_named_by_its_label()
    {
        var item = new TimelineItem(Ids.Of(1), AssetKinds.Photo, new DateTime(2019, 7, 14), true, null);

        var tile = Render<Sepiabox.Web.Components.Media.MediaCard>(p => p.Add(c => c.Item, item).Add(c => c.Href, "photos/x")).Find("a.thumb");

        Assert.Equal("true", tile.QuerySelector(".meta")!.GetAttribute("aria-hidden"));
        Assert.Contains("2019", tile.QuerySelector(".meta")!.TextContent, StringComparison.Ordinal);
        Assert.NotNull(tile.GetAttribute("aria-label"));
    }

    [Fact]
    public void Gives_no_date_to_an_undated_tile_and_keeps_the_duration_badge_on_a_video()
    {
        var video = new TimelineItem(Ids.Of(2), AssetKinds.Video, null, true, 65);

        var tile = Render<Sepiabox.Web.Components.Media.MediaCard>(p => p.Add(c => c.Item, video).Add(c => c.Href, "photos/x")).Find("a.thumb");

        Assert.Null(tile.QuerySelector(".meta"));
        Assert.Equal("1:05", tile.QuerySelector(".badge")!.TextContent);
    }

    [Fact]
    public void Counts_the_photos_under_each_month_heading_outside_the_heading_text()
    {
        var items = new[]
        {
            new TimelineItem(Ids.Of(1), AssetKinds.Photo, new DateTime(2019, 7, 14), true, null),
            new TimelineItem(Ids.Of(2), AssetKinds.Photo, new DateTime(2019, 7, 2), true, null),
            new TimelineItem(Ids.Of(3), AssetKinds.Photo, new DateTime(2019, 6, 2), true, null),
        };

        var page = Render<Sepiabox.Web.Components.Media.MonthGroups>(p => p.Add(c => c.Items, items));

        Assert.Equal(["July 2019", "June 2019"], page.FindAll("section.month h2").Select(h => h.TextContent));
        Assert.Equal(["2", "1"], page.FindAll("section.month .month-head .n").Select(n => n.TextContent));
    }
}
