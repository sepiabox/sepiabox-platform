using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Components.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The albums in the DSL, the links, the chips and the filter bar (S-052, D-114).</summary>
public sealed class AlbumFilterTests : LocalizedContext
{
    [Theory]
    [InlineData("""{"filters":{"albums":["Summer 2019"]}}""", null)]
    [InlineData("""{"filters":{"albums":["Summer 2019","Wedding"]}}""", null)]
    [InlineData("""{"filters":{"albums":[]}}""", "filters.albums must have 1 to 50 values.")]
    [InlineData("""{"filters":{"albums":[" "]}}""", "filters.albums: ' ' must be a name of 1 to 200 characters without control characters.")]
    public void Validates_the_albums_filter(string json, string? expected)
    {
        var valid = SearchQueryJson.TryParse(json, out var query, out var error);

        Assert.Equal(expected is null, valid);
        if (expected is null)
        {
            Assert.True(SearchQueryJson.TryParse(SearchQueryJson.Serialize(query), out var again, out _));
            Assert.Equal(query, again);
            Assert.False(query.Filters.IsEmpty);
        }
        else
        {
            Assert.Equal(expected, error);
        }
    }

    [Fact]
    public void Turns_the_filter_bar_s_album_into_a_query_and_back()
    {
        var query = SearchLinks.FromFields(null, null, null, null, null, album: " Summer 2019 ");

        Assert.Equal(["Summer 2019"], query.Filters.Albums);
        Assert.Equal("Summer 2019", SearchLinks.ToFields(query).Album);
        Assert.Null(SearchLinks.FromFields(null, null, null, null, null, album: " ").Filters.Albums);
        // The bar shows one album; a query with two shows none, and keeps both until the bar is submitted.
        Assert.Null(SearchLinks.ToFields(new SearchQuery { Filters = new SearchFilters { Albums = ["A", "B"] } }).Album);
    }

    [Fact]
    public void Shows_the_album_as_a_removable_chip_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        var query = new SearchQuery { Filters = new SearchFilters { Albums = ["Verão 2019", "Casamento"], Years = [2019] } };

        var chips = SearchChips.For(query, Cultures.Text());

        var chip = Assert.Single(chips, c => c.Label.StartsWith("Álbum", StringComparison.Ordinal));
        Assert.Equal("Álbum: Verão 2019, Casamento", chip.Label);
        Assert.Equal(new SearchQuery { Filters = new SearchFilters { Years = [2019] } }, chip.Without);
    }

    [Fact]
    public void Offers_an_album_selector_only_when_the_catalog_has_albums_and_keeps_one_the_address_names()
    {
        var options = new FilterOptions([], [], []) { Albums = ["Summer 2019", "Wedding"] };
        var fields = new FilterFields(null, null, null, null, null) { Album = "wedding" };

        var bar = Render<FilterBar>(p => p.Add(b => b.Options, options).Add(b => b.Fields, fields));

        Assert.Equal(["", "Summer 2019", "Wedding"], bar.FindAll("input[name=album]").Select(o => o.GetAttribute("value")));
        Assert.Equal(["Wedding"], bar.FindAll("input[name=album][checked]").Select(o => o.GetAttribute("value")));
        Assert.Equal("Album", bar.Find("input[name=album]").Closest("div.pop")!.GetAttribute("aria-label"));
        Assert.Empty(Render<FilterBar>(p => p.Add(b => b.Options, new FilterOptions([], [], []))).FindAll("input[name=album]"));
    }

    [Fact]
    public void Still_offers_and_selects_the_album_the_address_names_when_the_catalog_does_not_list_it()
    {
        var options = new FilterOptions([], [], []) { Albums = ["Summer 2019"] };
        var fields = new FilterFields(null, null, null, null, null) { Album = "Reunion" };

        var bar = Render<FilterBar>(p => p.Add(b => b.Options, options).Add(b => b.Fields, fields));

        Assert.Equal(["", "Reunion", "Summer 2019"], bar.FindAll("input[name=album]").Select(o => o.GetAttribute("value")));
        Assert.Equal(["Reunion"], bar.FindAll("input[name=album][checked]").Select(o => o.GetAttribute("value")));
    }

    [Fact]
    public void Lists_the_albums_alphabetically_ignoring_case_after_any()
    {
        var options = new FilterOptions([], [], []) { Albums = ["wedding", "Zoo", "Álbum", "Summer"] };

        var bar = Render<FilterBar>(p => p.Add(b => b.Options, options));

        Assert.Equal(["", "Álbum", "Summer", "wedding", "Zoo"], bar.FindAll("input[name=album]").Select(o => o.GetAttribute("value")));
    }

    [Theory]
    [InlineData("Praia\u202E 2019", "Praia 2019")]
    [InlineData("a\u2066b\u2069c\u200Ed\u200Fe\u061Cf", "abcdef")]
    [InlineData("a\u200Bb\u2060c\uFEFFd", "abcd")]
    [InlineData("👨\u200D👩 família", "👨\u200D👩 família")]
    [InlineData("Férias na praia", "Férias na praia")]
    [InlineData(null, null)]
    public void Takes_direction_controls_out_of_the_text_it_shows(string? text, string? expected)
    {
        Assert.Equal(expected, TextSafety.WithoutBidiControls(text));
    }

    [Fact]
    public void Names_the_album_selector_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        var options = new FilterOptions([], [], []) { Albums = ["Verão 2019"] };

        var bar = Render<FilterBar>(p => p.Add(b => b.Options, options));

        Assert.Equal("Álbum", bar.Find("input[name=album]").Closest("div.pop")!.GetAttribute("aria-label"));
    }
}

/// <summary>The albums on a catalog: the filter, the bar's list and the photo's page (S-052).</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class AlbumsCatalogTests(CatalogFixture catalog)
{
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Filters_by_album_ignoring_case_and_matches_assets_in_any_of_the_albums()
    {
        await using var db = await CatalogWithAlbumsAsync();

        Assert.Equal([1, 3], (await IdsAsync(db, new SearchFilters { Albums = ["summer 2019"] })).Order());
        Assert.Equal([1, 2], (await IdsAsync(db, new SearchFilters { Albums = ["WEDDING"] })).Order());
        Assert.Equal([1, 2, 3], (await IdsAsync(db, new SearchFilters { Albums = ["Summer 2019", "Wedding"] })).Order());
        Assert.Empty(await IdsAsync(db, new SearchFilters { Albums = ["No such album"] }));
    }

    [Fact]
    public async Task Combines_the_album_with_the_other_filters_and_counts_by_it()
    {
        await using var db = await CatalogWithAlbumsAsync();

        Assert.Equal([3], await IdsAsync(db, new SearchFilters { Albums = ["Summer 2019"], Kinds = ["video"] }));
        var count = await new SearchService(db, s_clock).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { Albums = ["Summer 2019"] },
            Aggregate = new SearchAggregate(),
        });
        Assert.Equal(2, count!.Total);
    }

    [Fact]
    public async Task Lists_the_albums_for_the_filter_bar_and_gives_a_photo_its_caption_and_albums()
    {
        await using var db = await CatalogWithAlbumsAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var options = await queries.GetFilterOptionsAsync();
        var detail = await queries.GetAssetAsync(Ids.Of(1));

        Assert.Equal(["Summer 2019", "Wedding"], options.Albums);
        Assert.Equal("Praia", detail!.Caption);
        Assert.Equal(["Summer 2019", "Wedding"], detail.Albums);
        Assert.Empty((await queries.GetAssetAsync(Ids.Of(4)))!.Albums);
    }

    [Fact]
    public async Task Lists_names_that_differ_only_in_case_once_and_leaves_out_direction_controls()
    {
        await using var db = await CatalogWithAlbumsAsync();
        db.AssetAlbums.AddRange(
            new AssetAlbum { AssetId = Ids.Of(4), Name = "Verão" },
            new AssetAlbum { AssetId = Ids.Of(4), Name = "verão" },
            new AssetAlbum { AssetId = Ids.Of(4), Name = "Praia\u202E" });
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var detail = await queries.GetAssetAsync(Ids.Of(4));

        Assert.Equal(["Praia", "Verão"], detail!.Albums);
    }

    [Fact]
    public async Task Takes_direction_controls_out_of_a_caption_and_maps_where_the_date_came_from()
    {
        await using var db = await CatalogWithAlbumsAsync();
        await db.Assets.Where(a => a.Id == Ids.Of(4)).ExecuteUpdateAsync(set => set
            .SetProperty(a => a.Caption, "Praia\u202E 2019")
            .SetProperty(a => a.CapturedAtSource, "export"));
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var detail = await queries.GetAssetAsync(Ids.Of(4));

        Assert.Equal("Praia 2019", detail!.Caption);
        Assert.Equal("export", detail.CapturedAtSource);
        Assert.Null((await queries.GetAssetAsync(Ids.Of(2)))!.CapturedAtSource);
    }

    [Fact]
    public async Task Lists_the_albums_most_used_first_without_a_name_over_200_characters_and_no_more_than_the_bar_holds()
    {
        await using var db = await CatalogWithAlbumsAsync();
        var tooLong = new string('x', SearchQueryJson.MaxNameLength + 1);
        // Inserted directly: the importer would have cut the name, but the filter must never offer one the DSL refuses.
        db.AssetAlbums.Add(new AssetAlbum { AssetId = Ids.Of(1), Name = tooLong });
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var options = await queries.GetFilterOptionsAsync();

        Assert.Equal(["Summer 2019", "Wedding"], options.Albums);
        Assert.DoesNotContain(tooLong, options.Albums);

        db.AssetAlbums.AddRange(Enumerable.Range(0, LibraryQueries.MaxAlbums + 2).Select(n => new AssetAlbum { AssetId = Ids.Of(4), Name = $"Album {n:000}" }));
        db.AssetAlbums.Add(new AssetAlbum { AssetId = Ids.Of(2), Name = "Album 501" });
        await db.SaveChangesAsync();
        var capped = (await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db))).GetFilterOptionsAsync()).Albums;

        Assert.Equal(LibraryQueries.MaxAlbums, capped.Count);
        // What survives the cap is the most used (the bar then sorts them by name): the three used twice, then the first names of
        // those used once, and the rest are cut.
        Assert.Contains("Album 501", capped);
        Assert.Contains("Summer 2019", capped);
        Assert.Contains("Wedding", capped);
        Assert.Contains("Album 496", capped);
        Assert.DoesNotContain("Album 497", capped);
    }

    [Fact]
    public async Task Offers_spellings_that_differ_only_in_case_as_one_album_in_the_most_used_spelling()
    {
        await using var db = await CatalogWithAlbumsAsync();
        db.AssetAlbums.AddRange(
            new AssetAlbum { AssetId = Ids.Of(1), Name = "verão" },
            new AssetAlbum { AssetId = Ids.Of(2), Name = "Verão" },
            new AssetAlbum { AssetId = Ids.Of(3), Name = "Verão" });
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var albums = (await queries.GetFilterOptionsAsync()).Albums;

        // One album, in the spelling two of its three photos use.
        Assert.Equal(["Summer 2019", "Verão", "Wedding"], albums);
    }

    [Fact]
    public async Task Offers_the_spelling_that_sorts_first_when_two_spellings_are_used_as_often()
    {
        await using var db = await CatalogWithAlbumsAsync();
        db.AssetAlbums.AddRange(
            new AssetAlbum { AssetId = Ids.Of(1), Name = "praia" },
            new AssetAlbum { AssetId = Ids.Of(2), Name = "Praia" });
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var albums = (await queries.GetFilterOptionsAsync()).Albums;

        Assert.Equal(["Praia", "Summer 2019", "Wedding"], albums);
    }

    [Fact]
    public async Task Counts_every_spelling_of_an_album_when_choosing_which_albums_the_bar_holds()
    {
        await using var db = await CatalogWithAlbumsAsync();
        // Two spellings used once each make an album used twice, as often as "Summer 2019", "Wedding" and the fillers, so more
        // albums are used twice than the bar holds and the ordinal tie-break keeps "A". Counted by its most used spelling only,
        // it would be used once and cut.
        db.AssetAlbums.AddRange(
            new AssetAlbum { AssetId = Ids.Of(1), Name = "a" },
            new AssetAlbum { AssetId = Ids.Of(2), Name = "A" });
        db.AssetAlbums.AddRange(Enumerable.Range(0, LibraryQueries.MaxAlbums - 1)
            .SelectMany(n => new[] { Ids.Of(1), Ids.Of(2) }.Select(id => new AssetAlbum { AssetId = id, Name = $"Filler {n:000}" })));
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var albums = (await queries.GetFilterOptionsAsync()).Albums;

        Assert.Equal(LibraryQueries.MaxAlbums, albums.Count);
        Assert.Contains("A", albums);
    }

    // Assets 1 (a photo) and 3 (a video) in "Summer 2019", asset 2 in "Wedding", and asset 1 in both; asset 4 in none.
    private async Task<CatalogDbContext> CatalogWithAlbumsAsync()
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        var one = CatalogData.Photo(Ids.Of(1), new DateTime(2019, 7, 14, 9, 0, 0));
        one.Caption = "Praia";
        db.Assets.AddRange(
            one,
            CatalogData.Photo(Ids.Of(2), new DateTime(2019, 8, 1, 10, 0, 0)),
            CatalogData.Video(Ids.Of(3), new DateTime(2019, 7, 15, 10, 0, 0)),
            CatalogData.Photo(Ids.Of(4), null));
        await db.SaveChangesAsync();
        db.AssetAlbums.AddRange(
            new AssetAlbum { AssetId = Ids.Of(1), Name = "Summer 2019" },
            new AssetAlbum { AssetId = Ids.Of(1), Name = "Wedding" },
            new AssetAlbum { AssetId = Ids.Of(2), Name = "Wedding" },
            new AssetAlbum { AssetId = Ids.Of(3), Name = "Summer 2019" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static async Task<List<int>> IdsAsync(CatalogDbContext db, SearchFilters filters)
    {
        var result = await new SearchService(db, s_clock).ExecuteAsync(new SearchQuery { Filters = filters });
        return [.. result.Hits.Select(h => int.Parse(h.Id[^8..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture))];
    }
}
