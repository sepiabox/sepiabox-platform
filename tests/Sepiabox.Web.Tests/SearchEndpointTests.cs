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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refuses_a_body_larger_than_the_limit_whether_or_not_it_announces_its_length(bool announced)
    {
        await using var web = new WebFactory(new FakeLibrary());
        using var http = web.CreateSignedInClient();
        // Valid JSON padded with white space, so only the size can be what is refused.
        var huge = Encoding.UTF8.GetBytes("{" + new string(' ', SearchEndpoints.MaxBodyBytes) + "}");
        HttpContent content = announced ? new ByteArrayContent(huge) : new StreamContent(new UnknownLengthStream(huge));
        content.Headers.ContentType = new("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/search", UriKind.Relative)) { Content = content };
        // Sent in chunks with no Content-Length, so only the streamed count can stop it.
        request.Headers.TransferEncodingChunked = !announced;

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"The search is larger than {SearchEndpoints.MaxBodyBytes} bytes.", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("""{"filters": {"dateTo": "9999-12-31"}}""", HttpStatusCode.OK)]
    [InlineData("""{"filters": {"places": {"cities": ["Lis\u0000bon"]}}}""", HttpStatusCode.BadRequest)]
    public async Task Answers_every_validated_query_and_refuses_control_characters(string json, HttpStatusCode expected)
    {
        await using var web = new WebFactory(new FakeLibrary());
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(new Uri("/api/search", UriKind.Relative), Json(json));

        Assert.Equal(expected, response.StatusCode);
    }

    /// <summary>A body with no length the client can announce, so it goes chunked.</summary>
    private sealed class UnknownLengthStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
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

    [Fact]
    public async Task Answers_503_with_a_message_when_search_by_description_is_unavailable()
    {
        var library = new FakeLibrary { SearchFailure = new Sepiabox.Catalog.Search.VisualSearchUnavailableException("No model.") };
        await using var web = new WebFactory(library);
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(new Uri("/api/search", UriKind.Relative), Json("""{"filters": {"visual": {"text": "snow"}}}"""));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("filters.visual", problem.GetProperty("field").GetString());
        Assert.StartsWith("Search by description is not available right now.", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
