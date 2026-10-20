using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary><c>POST /api/search</c> (S-013) on the real app with an in-memory library.</summary>
public sealed class SearchEndpointTests
{
    private static readonly TimelineItem Found =
        new(Ids.Of(41), AssetKinds.Video, new DateTime(2019, 12, 24, 20, 0, 0), true, DurationSeconds: 3);

    [Fact]
    public async Task Returns_the_assets_the_search_finds_when_the_dsl_is_valid()
    {
        var library = new FakeLibrary { SearchResults = new SearchPage([Found], HasMore: true) };
        await using var web = new WebFactory(library);
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(new Uri("/api/search", UriKind.Relative), Json("""
            {"filters": {"places": {"cities": ["Lisbon"]}, "months": [12]}, "limit": 1}
            """));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(Ids.Of(41), item.GetProperty("id").GetString());
        Assert.Equal("video", item.GetProperty("kind").GetString());
        Assert.Equal("2019-12-24T20:00:00", item.GetProperty("capturedAt").GetString());
        Assert.True(body.RootElement.GetProperty("hasMore").GetBoolean());
        var query = Assert.Single(library.Searches);
        Assert.Equal(["Lisbon"], query.Filters.Places!.Cities!);
        Assert.Equal([12], query.Filters.Months!);
        Assert.Equal(1, query.Limit);
    }

    [Theory]
    [InlineData("""{"filters": {"faces": ["Ana"]}}""", "faces")]
    [InlineData("""{"filters": {"months": [13]}}""", "filters.months")]
    [InlineData("""{"filters": """, "JSON")]
    public async Task Answers_400_with_a_message_when_the_dsl_is_invalid(string json, string mentioned)
    {
        var library = new FakeLibrary();
        await using var web = new WebFactory(library);
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(new Uri("/api/search", UriKind.Relative), Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(mentioned, problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(library.Searches);
    }

    [Fact]
    public async Task Refuses_a_body_that_is_not_json()
    {
        await using var web = new WebFactory(new FakeLibrary());
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative), new StringContent("{}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Refuses_a_body_larger_than_the_limit()
    {
        await using var web = new WebFactory(new FakeLibrary());
        using var http = web.CreateSignedInClient();
        var huge = "{\"filters\": {\"sources\": [\"" + new string('a', SearchEndpoints.MaxBodyBytes) + "\"]}}";

        using var response = await http.PostAsync(new Uri("/api/search", UriKind.Relative), Json(huge));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Searches_nothing_for_someone_not_signed_in_or_not_on_the_list()
    {
        var library = new FakeLibrary();
        await using var web = new WebFactory(library);
        using var anonymous = web.CreateAnonymousClient();
        using var stranger = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var fromAnonymous = await anonymous.PostAsync(new Uri("/api/search", UriKind.Relative), Json("{}"));
        using var fromStranger = await stranger.PostAsync(new Uri("/api/search", UriKind.Relative), Json("{}"));

        Assert.NotEqual(HttpStatusCode.OK, fromAnonymous.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, fromStranger.StatusCode);
        Assert.Empty(library.Searches);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
