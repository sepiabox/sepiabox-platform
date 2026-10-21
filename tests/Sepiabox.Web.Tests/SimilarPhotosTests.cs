using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>"More photos similar to this one" (S-017) on a real PostgreSQL catalog with synthetic embeddings.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SimilarPhotosTests(CatalogFixture catalog)
{
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 21, 9, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(SimilarityMode.Ranking)]
    [InlineData(SimilarityMode.Exact)]
    public async Task Returns_the_others_most_similar_first_without_the_photo_itself(SimilarityMode mode)
    {
        await using var db = await LibraryAsync();

        var result = await new SearchService(db, s_clock).ExecuteAsync(Similar(1), mode);

        // Every other photo with an embedding, however far (even the opposite one); 7 has none.
        Assert.Equal([Ids.Of(2), Ids.Of(3), Ids.Of(4), Ids.Of(5), Ids.Of(6)], result.Hits.Select(h => h.Id));
        Assert.DoesNotContain(Ids.Of(1), result.Hits.Select(h => h.Id));
        Assert.Equal(result.Hits.Select(h => h.Score).OrderDescending(), result.Hits.Select(h => h.Score));
        Assert.InRange(result.Hits[0].Score!.Value, 0.94, 0.96);
        Assert.InRange(result.Hits[^1].Score!.Value, -1.001, -0.999);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task Returns_only_the_2020_assets_in_similarity_order_when_combined_with_years()
    {
        await using var db = await LibraryAsync();
        var query = Similar(1) with { Filters = Similar(1).Filters with { Years = [2020] } };

        var result = await new SearchService(db, s_clock).ExecuteAsync(query);

        // Asset 3 is the second most similar but from 2019.
        Assert.Equal([Ids.Of(2), Ids.Of(4), Ids.Of(5), Ids.Of(6)], result.Hits.Select(h => h.Id));
        Assert.All(result.Hits, h => Assert.Equal(2020, h.CapturedAt!.Value.Year));
    }

    [Fact]
    public async Task Returns_at_most_its_limit_and_says_more_matched()
    {
        await using var db = await LibraryAsync();
        var query = new SearchQuery { Filters = new SearchFilters { SimilarTo = new SimilarToFilter { AssetId = Ids.Of(1), Limit = 2 } } };

        var result = await new SearchService(db, s_clock).ExecuteAsync(query);
        var capped = await new SearchService(db, s_clock).ExecuteAsync(query with { Limit = 1 });

        Assert.Equal([Ids.Of(2), Ids.Of(3)], result.Hits.Select(h => h.Id));
        Assert.True(result.HasMore);
        Assert.Equal([Ids.Of(2)], capped.Hits.Select(h => h.Id));
    }

    [Fact]
    public async Task Refuses_an_asset_without_an_embedding_and_one_not_in_the_catalog()
    {
        await using var db = await LibraryAsync();
        var service = new SearchService(db, s_clock);

        var notAnalysed = await Assert.ThrowsAsync<AssetNotAnalysedException>(() => service.ExecuteAsync(Similar(7)));
        var unknown = await Assert.ThrowsAsync<AssetNotAnalysedException>(() => service.ExecuteAsync(Similar(99)));

        Assert.True(notAnalysed.Exists);
        Assert.Equal(Ids.Of(7), notAnalysed.AssetId);
        Assert.False(unknown.Exists);
        Assert.Null(await service.CountAsync(Similar(1)));
    }

    [Fact]
    public async Task The_detail_page_links_to_similar_photos_and_the_page_and_the_api_show_them()
    {
        string connection;
        await using (var db = await LibraryAsync())
        {
            connection = catalog.ConnectionStringFor(db);
        }

        await using var web = new WebFactory(connection);
        using var http = web.CreateSignedInClient();
        var link = SearchLinks.Timeline(Similar(1) with { Filters = Similar(1).Filters with { Years = [2020] } });

        var detail = await http.GetStringAsync(new Uri($"/photos/{Ids.Of(1)}", UriKind.Relative));
        var unanalysed = await http.GetStringAsync(new Uri($"/photos/{Ids.Of(7)}", UriKind.Relative));
        var html = await http.GetStringAsync(new Uri(link[1..], UriKind.Relative));
        var stale = await http.GetStringAsync(new Uri(SearchLinks.Timeline(Similar(7))[1..], UriKind.Relative));
        using var api = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative),
            new StringContent($$$"""{"filters": {"similarTo": {"assetId": "{{{Ids.Of(1)}}}"}, "years": [2020]}}""", System.Text.Encoding.UTF8, "application/json"));
        using var apiUnanalysed = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative),
            new StringContent($$$$"""{"filters": {"similarTo": {"assetId": "{{{{Ids.Of(7)}}}}"}}}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Contains($"href=\"{SearchLinks.Timeline(Similar(1))}\"", detail, StringComparison.Ordinal);
        Assert.Contains("This photo has not been analysed yet.", unanalysed, StringComparison.Ordinal);
        Assert.Contains("Most similar to the photo", html, StringComparison.Ordinal);
        var positions = new[] { 2, 4, 5, 6 }.Select(n => html.IndexOf($"photos/{Ids.Of(n)}", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.Order(), positions);
        Assert.DoesNotContain($"photos/{Ids.Of(3)}", html, StringComparison.Ordinal);
        Assert.Contains("This photo has not been analysed yet.", stale, StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.OK, api.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await api.Content.ReadAsStringAsync());
        Assert.Equal([Ids.Of(2), Ids.Of(4), Ids.Of(5), Ids.Of(6)], body.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, apiUnanalysed.StatusCode);
    }

    [Theory]
    [InlineData(SimilarityMode.Ranking)]
    [InlineData(SimilarityMode.Exact)]
    public async Task Orders_equally_similar_assets_by_id(SimilarityMode mode)
    {
        await using var db = await LibraryAsync();
        // Two copies of one picture's embedding, the later id stored first.
        var tied = FakeQueryEmbedder.Axis(1, 0.7f, 2, 0.714f);
        await AddAsync(db, 9, 2020, "photo", tied);
        await AddAsync(db, 8, 2020, "photo", tied);

        var result = await new SearchService(db, s_clock).ExecuteAsync(Similar(1), mode);

        var ids = result.Hits.Select(h => h.Id).ToList();
        Assert.Equal(ids.IndexOf(Ids.Of(8)) + 1, ids.IndexOf(Ids.Of(9)));
        Assert.Equal(result.Hits[ids.IndexOf(Ids.Of(8))].Score, result.Hits[ids.IndexOf(Ids.Of(9))].Score);
        Assert.Equal([Ids.Of(2), Ids.Of(3), Ids.Of(8), Ids.Of(9), Ids.Of(4), Ids.Of(5), Ids.Of(6)], ids);
    }

    [Fact]
    public async Task Finds_a_video_among_a_photo_s_similar_ones_and_the_photos_similar_to_the_video()
    {
        string connection;
        await using (var db = await LibraryAsync())
        {
            // A video whose poster is nearly photo 1.
            await AddAsync(db, 10, 2020, "video", FakeQueryEmbedder.Axis(1, 0.99f, 2, 0.14f));
            var service = new SearchService(db, s_clock);

            var likePhoto = await service.ExecuteAsync(Similar(1));
            var likeVideo = await service.ExecuteAsync(Similar(10));

            Assert.Equal(Ids.Of(10), likePhoto.Hits[0].Id);
            Assert.Equal("video", likePhoto.Hits[0].Kind);
            Assert.Equal(Ids.Of(1), likeVideo.Hits[0].Id);
            Assert.DoesNotContain(Ids.Of(10), likeVideo.Hits.Select(h => h.Id));
            connection = catalog.ConnectionStringFor(db);
        }

        await using var web = new WebFactory(connection);
        using var http = web.CreateSignedInClient();

        var detail = await http.GetStringAsync(new Uri($"/photos/{Ids.Of(10)}", UriKind.Relative));
        var html = await http.GetStringAsync(new Uri(SearchLinks.Timeline(Similar(10))[1..], UriKind.Relative));

        Assert.Contains($"href=\"{SearchLinks.Timeline(Similar(10))}\">Similar</a>", detail, StringComparison.Ordinal);
        Assert.Contains("Most similar to the photo", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf($"photos/{Ids.Of(1)}", StringComparison.Ordinal) < html.IndexOf($"photos/{Ids.Of(2)}", StringComparison.Ordinal));
    }

    private static SearchQuery Similar(int n) => SearchLinks.Similar(Ids.Of(n));

    private static async Task AddAsync(CatalogDbContext db, int n, int year, string kind, float[] vector)
    {
        db.Assets.Add(Asset(n, year, kind));
        db.AssetEmbeddings.Add(Embedding(n, vector));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    // Photo 1 (2020) on axis 1; 2 (2020) about 0.95 similar to it; 3 (2019) about 0.9; 4 (2020) 0.6; 5 (2020)
    // orthogonal; 6 (2020) opposite; 7 (2020) without an embedding.
    private async Task<CatalogDbContext> LibraryAsync()
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(
            Asset(1, 2020), Asset(2, 2020), Asset(3, 2019), Asset(4, 2020), Asset(5, 2020), Asset(6, 2020), Asset(7, 2020));
        db.AssetEmbeddings.AddRange(
            Embedding(1, FakeQueryEmbedder.Axis(1)),
            Embedding(2, FakeQueryEmbedder.Axis(1, 0.95f, 2, 0.31f)),
            Embedding(3, FakeQueryEmbedder.Axis(1, 0.9f, 2, 0.44f)),
            Embedding(4, FakeQueryEmbedder.Axis(1, 0.6f, 2, 0.8f)),
            Embedding(5, FakeQueryEmbedder.Axis(0)),
            Embedding(6, FakeQueryEmbedder.Axis(1, -1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static AssetEmbedding Embedding(int n, float[] vector) =>
        new() { AssetId = Ids.Of(n), Model = PgvectorSimilarity.ModelKey, Embedding = new Pgvector.Vector(vector) };

    private static Asset Asset(int n, int year, string kind = "photo") => new()
    {
        Id = Ids.Of(n),
        Kind = kind,
        ContentType = kind == "video" ? "video/mp4" : "image/jpeg",
        SizeBytes = 1000,
        CapturedAt = new DateTime(year, 1, n),
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Sources = [new AssetSource { AssetId = Ids.Of(n), Type = "local", Name = "HD", Path = $"{n}.jpg" }],
    };
}
