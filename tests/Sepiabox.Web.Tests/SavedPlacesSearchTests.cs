using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.SavedPlaces;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The family's places on a real PostgreSQL with PostGIS (S-014): radius, distance, the home mark, uniqueness and deletion.
/// Every coordinate is made up.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SavedPlacesSearchTests(CatalogFixture catalog)
{
    // A made-up home and the length of a degree of latitude there (WGS 84 meridian arc at 38.7°, rounded).
    private const double HomeLatitude = 38.74;
    private const double HomeLongitude = -9.16;
    private const double MetresPerDegree = 111_020;
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Finds_only_the_assets_inside_a_place_s_200_metre_radius()
    {
        await using var db = await CatalogAsync((1, 150), (2, 190), (3, 250), (4, null));
        Assert.Equal(SavedPlaceOutcome.Done, (await Editor(db).CreateAsync("Casa da praia", HomeLatitude, HomeLongitude, 200, false)).Outcome);

        var found = await IdsAsync(db, new SearchFilters { SavedPlaces = ["CASA DA PRAIA"] });

        Assert.Equal([Ids.Of(2), Ids.Of(1)], found);
    }

    [Fact]
    public async Task Uses_each_place_s_own_radius_when_searching_several_places()
    {
        // A 1,000 m place at home and a 200 m one 10 km north, in one search: a fixed radius of either size fails.
        await using var db = await CatalogAsync((1, 600), (2, 1_100), (3, 10_150), (4, 10_300));
        await Editor(db).CreateAsync("Quinta", HomeLatitude, HomeLongitude, 1_000, isHome: false);
        await Editor(db).CreateAsync("Café", HomeLatitude + (10_000 / MetresPerDegree), HomeLongitude, 200, isHome: false);

        var found = await IdsAsync(db, new SearchFilters { SavedPlaces = ["Quinta", "Café"] });

        Assert.Equal([Ids.Of(3), Ids.Of(1)], found);
    }

    [Fact]
    public async Task Leaves_one_home_when_several_places_are_marked_at_once()
    {
        await using var db = await CatalogAsync();
        var ids = new List<long>();
        for (var i = 0; i < 8; i++)
        {
            ids.Add((await Editor(db).CreateAsync($"Lugar {i}", HomeLatitude, HomeLongitude, 300, isHome: false)).Id!.Value);
        }

        var outcomes = await Task.WhenAll(ids.Select(async id =>
        {
            await using var own = new TestContexts(catalog.ConnectionStringFor(db)).CreateDbContext();
            return await new SavedPlaceEditor(own).UpdateAsync(id, $"Lugar {ids.IndexOf(id)}", null, isHome: true);
        }));

        Assert.All(outcomes, outcome => Assert.Equal(SavedPlaceOutcome.Done, outcome));
        Assert.Single(await db.SavedPlaces.Where(p => p.IsHome).ToListAsync());
        Assert.All(await db.SavedPlaces.ToListAsync(), p => Assert.Equal(300, p.RadiusM));
    }

    [Fact]
    public async Task Keeps_names_short_enough_to_be_a_label_not_an_address()
    {
        await using var db = await CatalogAsync();

        Assert.Equal(SavedPlaceOutcome.Done, (await Editor(db).CreateAsync(new string('a', 60), 0, 0, 300, isHome: false)).Outcome);
        Assert.Equal(SavedPlaceOutcome.NameInvalid, (await Editor(db).CreateAsync(new string('b', 61), 0, 0, 300, isHome: false)).Outcome);
    }

    [Fact]
    public async Task Finds_only_the_assets_more_than_100_km_from_home()
    {
        await using var db = await CatalogAsync((1, 95_000), (2, 105_000), (3, 500_000), (4, 0), (5, null));
        await Editor(db).CreateAsync("Casa", HomeLatitude, HomeLongitude, 300, isHome: true);
        await Editor(db).CreateAsync("Casa da praia", HomeLatitude + 0.2, HomeLongitude, 300, isHome: false);

        var far = await IdsAsync(db, new SearchFilters { DistanceFrom = new DistanceFilter { Home = true, MinKm = 100 } });
        var near = await IdsAsync(db, new SearchFilters { DistanceFrom = new DistanceFilter { Home = true, MaxKm = 100 } });
        var between = await IdsAsync(db, new SearchFilters { DistanceFrom = new DistanceFilter { Home = true, MinKm = 100, MaxKm = 200 } });
        var fromThePlace = await IdsAsync(db, new SearchFilters { DistanceFrom = new DistanceFilter { Place = "casa da praia", MaxKm = 50 } });

        Assert.Equal([Ids.Of(3), Ids.Of(2)], far);
        Assert.Equal([Ids.Of(4), Ids.Of(1)], near);
        Assert.Equal([Ids.Of(2)], between);
        // The beach house is about 22 km north of home: home is within 50 km of it, 95 km north of home (73 km) is not.
        Assert.Equal([Ids.Of(4)], fromThePlace);
    }

    [Fact]
    public async Task Keeps_the_home_mark_on_one_place_only()
    {
        await using var db = await CatalogAsync();
        var (_, first) = await Editor(db).CreateAsync("Casa", HomeLatitude, HomeLongitude, 300, isHome: true);
        var (_, second) = await Editor(db).CreateAsync("Apartamento", HomeLatitude + 1, HomeLongitude, 300, isHome: true);

        Assert.Equal([second!.Value], await db.SavedPlaces.Where(p => p.IsHome).Select(p => p.Id).ToListAsync());
        Assert.Equal(SavedPlaceOutcome.Done, await Editor(db).UpdateAsync(first!.Value, "Casa", 300, isHome: true));
        Assert.Equal([first.Value], await db.SavedPlaces.Where(p => p.IsHome).Select(p => p.Id).ToListAsync());
        Assert.Equal(SavedPlaceOutcome.Done, await Editor(db).UpdateAsync(first.Value, "Casa", 300, isHome: false));
        Assert.Empty(await db.SavedPlaces.Where(p => p.IsHome).ToListAsync());
    }

    [Fact]
    public async Task Leaves_the_assets_intact_when_a_place_is_deleted()
    {
        await using var db = await CatalogAsync((1, 50), (2, 1_000));
        var (_, id) = await Editor(db).CreateAsync("Casa", HomeLatitude, HomeLongitude, 300, isHome: true);
        var before = await db.Assets.AsNoTracking().OrderBy(a => a.Id).Select(a => new { a.Id, a.Location }).ToListAsync();

        Assert.Equal(SavedPlaceOutcome.Done, await Editor(db).DeleteAsync(id!.Value));

        var after = await db.Assets.AsNoTracking().OrderBy(a => a.Id).Select(a => new { a.Id, a.Location }).ToListAsync();
        Assert.Equal(before.Select(a => (a.Id, a.Location!.Y, a.Location.X)), after.Select(a => (a.Id, a.Location!.Y, a.Location.X)));
        Assert.Empty(await db.SavedPlaces.ToListAsync());
        Assert.Equal(SavedPlaceOutcome.NotFound, await Editor(db).DeleteAsync(id.Value));
        var unknown = await Assert.ThrowsAsync<UnknownPlacesException>(() => IdsAsync(db, new SearchFilters { SavedPlaces = ["Casa"] }));
        Assert.Equal(["Casa"], unknown.Missing);
    }

    [Theory]
    [InlineData("casa da PRAIA")]
    [InlineData("Casa  da praia")]
    public async Task Rejects_a_second_place_with_the_same_name_in_another_letter_case(string name)
    {
        await using var db = await CatalogAsync();
        await Editor(db).CreateAsync("Casa da praia", HomeLatitude, HomeLongitude, 300, isHome: false);
        var (_, other) = await Editor(db).CreateAsync("Outra", HomeLatitude, HomeLongitude, 300, isHome: false);

        Assert.Equal(SavedPlaceOutcome.NameTaken, (await Editor(db).CreateAsync(name, 0, 0, 300, isHome: false)).Outcome);
        Assert.Equal(SavedPlaceOutcome.NameTaken, await Editor(db).UpdateAsync(other!.Value, name, 300, isHome: false));
        Assert.Equal(2, await db.SavedPlaces.CountAsync());
    }

    [Theory]
    [InlineData("", 0, 0, 200, SavedPlaceOutcome.NameInvalid)]
    [InlineData("Casa", 91, 0, 200, SavedPlaceOutcome.LocationInvalid)]
    [InlineData("Casa", 0, -181, 200, SavedPlaceOutcome.LocationInvalid)]
    [InlineData("Casa", 0, 0, 9, SavedPlaceOutcome.RadiusInvalid)]
    [InlineData("Casa", 0, 0, 50_001, SavedPlaceOutcome.RadiusInvalid)]
    public async Task Refuses_a_place_without_a_name_a_point_on_earth_or_a_sensible_radius(
        string name, double latitude, double longitude, int radius, SavedPlaceOutcome expected)
    {
        await using var db = await CatalogAsync();

        Assert.Equal(expected, (await Editor(db).CreateAsync(name, latitude, longitude, radius, isHome: false)).Outcome);
        Assert.Empty(await db.SavedPlaces.ToListAsync());
    }

    [Fact]
    public async Task Names_every_unknown_place_and_a_missing_home()
    {
        await using var db = await CatalogAsync((1, 0));
        await Editor(db).CreateAsync("Casa da praia", HomeLatitude, HomeLongitude, 300, isHome: false);

        var unknown = await Assert.ThrowsAsync<UnknownPlacesException>(() => Search(db).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { SavedPlaces = ["Casa da praia", "Sítio"], DistanceFrom = new DistanceFilter { Home = true, MinKm = 1 } },
            Aggregate = new SearchAggregate(),
        }));

        Assert.Equal(["Sítio"], unknown.Missing);
        Assert.True(unknown.HomeMissing);
    }

    [Fact]
    public async Task Answers_400_naming_the_place_and_counts_a_place_over_the_years()
    {
        await using var db = await CatalogAsync((1, 50), (2, 80), (3, 5_000));
        await Editor(db).CreateAsync("Casa", HomeLatitude, HomeLongitude, 300, isHome: true);
        await db.Assets.Where(a => a.Id == Ids.Of(2)).ExecuteUpdateAsync(set => set.SetProperty(a => a.CapturedAt, new DateTime(2019, 5, 1)));
        await using var web = new WebFactory(catalog.ConnectionStringFor(db));
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative),
            JsonContent.Create(JsonDocument.Parse("""{"filters":{"savedPlaces":["Sítio"]}}""").RootElement));
        var years = await Search(db).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { SavedPlaces = ["Casa"] },
            Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Year },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["Sítio"], problem.GetProperty("missing").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal("filters.savedPlaces", problem.GetProperty("field").GetString());
        Assert.Equal([new CountGroup("2019", 1), new CountGroup("2025", 1)], years!.Groups);
    }

    [Fact]
    public async Task Lists_the_places_for_the_filter_bar_and_a_photo_s_location()
    {
        await using var db = await CatalogAsync((1, 50));
        await Editor(db).CreateAsync("Casa da praia", HomeLatitude + 0.2, HomeLongitude, 300, isHome: false);
        await Editor(db).CreateAsync("Casa", HomeLatitude, HomeLongitude, 300, isHome: true);
        var contexts = new TestContexts(catalog.ConnectionStringFor(db));
        var queries = new LibraryQueries(contexts, s_clock);

        var options = await queries.GetFilterOptionsAsync();
        var detail = await queries.GetAssetAsync(Ids.Of(1));
        var listed = await new SavedPlaces(contexts).ListAsync();

        Assert.Equal(["Casa", "Casa da praia"], options.SavedPlaces);
        Assert.Equal("Casa", options.HomePlace);
        Assert.Equal(HomeLatitude + (50 / MetresPerDegree), detail!.Location!.Value.Latitude, 6);
        Assert.Equal(HomeLongitude, detail.Location.Value.Longitude, 6);
        Assert.Equal([("Casa", true), ("Casa da praia", false)], listed.Select(p => (p.Name, p.IsHome)));
    }

    private static SavedPlaceEditor Editor(CatalogDbContext db) => new(db);

    private static SearchService Search(CatalogDbContext db) => new(db, s_clock);

    private static async Task<List<string>> IdsAsync(CatalogDbContext db, SearchFilters filters) =>
        [.. (await Search(db).ExecuteAsync(new SearchQuery { Filters = filters })).Hits.Select(h => h.Id)];

    // One photo per entry, on its own day of 2025, that many metres north of home, or without a location.
    private async Task<CatalogDbContext> CatalogAsync(params (int N, double? MetresNorth)[] photos)
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        foreach (var (n, metres) in photos)
        {
            var asset = CatalogData.Photo(Ids.Of(n), new DateTime(2025, 1, n, 12, 0, 0));
            asset.Location = metres is { } north ? SavedPlaceEditor.PointAt(HomeLatitude + (north / MetresPerDegree), HomeLongitude) : null;
            db.Assets.Add(asset);
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
