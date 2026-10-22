using System.Net;
using System.Text;
using System.Text.Json;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>"Photos like this part of a photo" (S-041, D-099) on a real PostgreSQL catalog with synthetic embeddings.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class RegionSearchCatalogTests(CatalogFixture catalog)
{
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(SimilarityMode.Ranking)]
    [InlineData(SimilarityMode.Exact)]
    public async Task Ranks_every_photo_by_the_region_s_vector_and_keeps_the_photo_the_region_is_in(SimilarityMode mode)
    {
        await using var db = await LibraryAsync();
        var embedder = new FixedRegionEmbedder(FakeQueryEmbedder.Axis(1));

        var result = await new SearchService(db, s_clock, regionEmbedder: embedder).ExecuteAsync(Region(5), mode);

        // Photo 5 is orthogonal to the region's vector, but it is the one the region was drawn on: it stays in, ranked last as any other would.
        Assert.Equal([Ids.Of(1), Ids.Of(2), Ids.Of(3), Ids.Of(4), Ids.Of(5)], result.Hits.Select(h => h.Id));
        Assert.Equal(result.Hits.Select(h => h.Score).OrderDescending(), result.Hits.Select(h => h.Score));
        var (assetId, box) = embedder.Asked.Single();
        Assert.Equal(Ids.Of(5), assetId);
        Assert.Equal([0.1, 0.2, 0.3, 0.4], box);
    }

    [Fact]
    public async Task Combines_with_the_other_filters_and_stops_at_its_limit()
    {
        await using var db = await LibraryAsync();
        var service = new SearchService(db, s_clock, regionEmbedder: new FixedRegionEmbedder(FakeQueryEmbedder.Axis(1)));
        var filtered = Region(5) with { Filters = Region(5).Filters with { Years = [2020] } };
        var limited = new SearchQuery { Filters = new SearchFilters { SimilarToRegion = Region(5).Filters.SimilarToRegion! with { Limit = 2 } } };

        var in2020 = await service.ExecuteAsync(filtered);
        var two = await service.ExecuteAsync(limited);

        Assert.Equal([Ids.Of(1), Ids.Of(2), Ids.Of(4), Ids.Of(5)], in2020.Hits.Select(h => h.Id));
        Assert.Equal([Ids.Of(1), Ids.Of(2)], two.Hits.Select(h => h.Id));
        Assert.True(two.HasMore);
        Assert.Null(await service.CountAsync(Region(5)));
    }

    [Fact]
    public async Task Says_so_when_the_image_model_is_off_and_when_the_photo_cannot_be_cut()
    {
        await using var db = await LibraryAsync();

        await Assert.ThrowsAsync<VisualSearchUnavailableException>(() => new SearchService(db, s_clock).ExecuteAsync(Region(5)));
        await Assert.ThrowsAsync<VisualSearchUnavailableException>(
            () => new SearchService(db, s_clock, regionEmbedder: new FixedRegionEmbedder(FakeQueryEmbedder.Axis(1), available: false)).ExecuteAsync(Region(5)));
        var unknown = await Assert.ThrowsAsync<AssetNotAnalysedException>(
            () => new SearchService(db, s_clock, regionEmbedder: new FixedRegionEmbedder(null)).ExecuteAsync(Region(99)));
        Assert.False(unknown.Exists);
    }

    [Fact]
    public async Task Tells_the_occasion_of_each_photo_and_leaves_out_the_ones_in_none()
    {
        await using var db = await LibraryAsync();

        var occasions = await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)))
            .GetOccasionsOfAsync([Ids.Of(1), Ids.Of(2), Ids.Of(3), Ids.Of(4), Ids.Of(5)]);

        Assert.Equal(new Dictionary<string, long> { [Ids.Of(1)] = 20, [Ids.Of(2)] = 10, [Ids.Of(3)] = 20, [Ids.Of(5)] = 10 }, occasions.ToDictionary(o => o.Key, o => o.Value.Id));
        Assert.Equal("Lisbon", occasions[Ids.Of(1)].PlaceLabel);
        Assert.Empty(await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db))).GetOccasionsOfAsync([]));
    }

    [Fact]
    public async Task The_page_groups_the_photos_by_occasion_and_the_api_refuses_a_box_outside_the_photo()
    {
        string connection;
        await using (var db = await LibraryAsync())
        {
            connection = catalog.ConnectionStringFor(db);
        }

        await using var web = new WebFactory(connection) { RegionEmbedder = new FixedRegionEmbedder(FakeQueryEmbedder.Axis(1)) };
        using var http = web.CreateSignedInClient();
        var link = SearchLinks.Timeline(Region(5));

        var html = await http.GetStringAsync(new Uri(link[1..], UriKind.Relative));
        using var ok = await Post(http, Body("[0.1, 0.2, 0.3, 0.4]"));
        using var outside = await Post(http, Body("[0.9, 0.2, 0.3, 0.4]"));
        using var flat = await Post(http, Body("[0.1, 0.2, 0, 0.4]"));

        Assert.Contains("Most similar to the part of the photo, by occasion", html, StringComparison.Ordinal);
        Assert.Contains("Similar to part of a photo", html, StringComparison.Ordinal);
        // The occasion of the most similar photo (1, in occasion 20) first, then occasion 10, then the photo in none (4).
        var headings = new[] { "occasions/20", "occasions/10", "Not in an occasion" }.Select(t => html.IndexOf(t, StringComparison.Ordinal)).ToList();
        Assert.All(headings, h => Assert.True(h >= 0));
        Assert.Equal(headings.Order(), headings);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var body = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.Equal([Ids.Of(1), Ids.Of(2), Ids.Of(3), Ids.Of(4), Ids.Of(5)], body.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, flat.StatusCode);
        Assert.Contains("similarToRegion.box", await outside.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_and_the_api_say_when_the_image_model_is_off()
    {
        string connection;
        await using (var db = await LibraryAsync())
        {
            connection = catalog.ConnectionStringFor(db);
        }

        await using var web = new WebFactory(connection) { RegionEmbedder = new FixedRegionEmbedder(FakeQueryEmbedder.Axis(1), available: false) };
        using var http = web.CreateSignedInClient();

        var html = await http.GetStringAsync(new Uri(SearchLinks.Timeline(Region(5))[1..], UriKind.Relative));
        using var api = await Post(http, Body("[0.1, 0.2, 0.3, 0.4]"));
        var detail = await http.GetStringAsync(new Uri($"/photos/{Ids.Of(5)}", UriKind.Relative));

        Assert.Contains("Search by a part of a photo is not available right now", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, api.StatusCode);
        Assert.Contains("filters.similarToRegion", await api.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("data-region-frame", detail, StringComparison.Ordinal);
    }

    private static string Body(string box) =>
        "{\"filters\": {\"similarToRegion\": {\"assetId\": \"" + Ids.Of(5) + "\", \"box\": " + box + "}}}";

    private static Task<HttpResponseMessage> Post(HttpClient http, string json) =>
        http.PostAsync(new Uri("/api/search", UriKind.Relative), new StringContent(json, Encoding.UTF8, "application/json"));

    private static SearchQuery Region(int n) => SearchLinks.Region(Ids.Of(n), [0.1, 0.2, 0.3, 0.4]);

    // Photo 1 (2020) on axis 1; 2 (2020) about 0.95 similar to it; 3 (2019) about 0.9; 4 (2020) 0.6 and in no occasion;
    // 5 (2020, the photo the region is in) orthogonal. Occasion 20 has 1 and 3, occasion 10 has 2 and 5.
    private async Task<CatalogDbContext> LibraryAsync()
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(Enumerable.Range(1, 5).Select(n => CatalogData.Photo(Ids.Of(n), new DateTime(n == 3 ? 2019 : 2020, 1, n))));
        db.AssetEmbeddings.AddRange(
            Embedding(1, FakeQueryEmbedder.Axis(1)),
            Embedding(2, FakeQueryEmbedder.Axis(1, 0.95f, 2, 0.31f)),
            Embedding(3, FakeQueryEmbedder.Axis(1, 0.9f, 2, 0.44f)),
            Embedding(4, FakeQueryEmbedder.Axis(1, 0.6f, 2, 0.8f)),
            Embedding(5, FakeQueryEmbedder.Axis(0)));
        db.Occasions.AddRange(
            new Occasion { Id = 20, StartsAt = new DateTime(2019, 1, 3), EndsAt = new DateTime(2020, 1, 1), PlaceLabel = "Lisbon", AssetCount = 2 },
            new Occasion { Id = 10, StartsAt = new DateTime(2020, 1, 2), EndsAt = new DateTime(2020, 1, 5), AssetCount = 2 });
        await db.SaveChangesAsync();
        db.OccasionAssets.AddRange(
            new OccasionAsset { EventId = 20, AssetId = Ids.Of(1) },
            new OccasionAsset { EventId = 20, AssetId = Ids.Of(3) },
            new OccasionAsset { EventId = 10, AssetId = Ids.Of(2) },
            new OccasionAsset { EventId = 10, AssetId = Ids.Of(5) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static AssetEmbedding Embedding(int n, float[] vector) =>
        new() { AssetId = Ids.Of(n), Model = PgvectorSimilarity.ModelKey, Embedding = new Pgvector.Vector(vector) };

    private sealed class FixedRegionEmbedder(float[]? vector, bool available = true) : IRegionEmbedder
    {
        public List<(string AssetId, IReadOnlyList<double> Box)> Asked { get; } = [];

        public bool IsAvailable => available;

        public Task<float[]> EmbedAsync(string assetId, IReadOnlyList<double> box, CancellationToken cancellationToken = default)
        {
            Asked.Add((assetId, box));
            return vector is null ? Task.FromException<float[]>(new AssetNotAnalysedException(assetId, null)) : Task.FromResult(vector);
        }
    }
}
