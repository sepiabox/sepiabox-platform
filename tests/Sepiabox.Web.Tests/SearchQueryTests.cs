using System.Text;
using System.Text.Json.Nodes;
using Json.Schema;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Tests;

/// <summary>The search DSL v1 (S-013): parsing, validation, the URL form and agreement with its JSON Schema.</summary>
public sealed class SearchQueryTests
{
    // The story's example, as the translator and the API send it.
    private const string StoryExample = """
        {
          "filters": {
            "kinds": ["photo", "video"],
            "dateFrom": "2019-01-01", "dateTo": "2019-12-31",
            "years": [2019], "months": [12],
            "onThisDay": { "yearsAgo": 5 },
            "places": { "cities": ["Lisbon"], "countries": ["PT"] },
            "sources": ["HD da Ana"],
            "undatedOnly": false
          },
          "sort": "capturedAtDesc",
          "limit": 200
        }
        """;

    private static readonly Lazy<JsonSchema> s_schema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(SourceTree.Root, "docs", "schemas", "search.v1.json"))));

    [Fact]
    public void Parses_every_field_of_the_story_s_example_when_valid()
    {
        Assert.True(SearchQueryJson.TryParse(StoryExample, out var query, out var error), error);

        var filters = query.Filters;
        Assert.Equal(["photo", "video"], filters.Kinds!);
        Assert.Equal((new DateOnly(2019, 1, 1), new DateOnly(2019, 12, 31)), (filters.DateFrom, filters.DateTo));
        Assert.Equal([2019], filters.Years!);
        Assert.Equal([12], filters.Months!);
        Assert.Equal(5, filters.OnThisDay!.YearsAgo);
        Assert.Equal(["Lisbon"], filters.Places!.Cities!);
        Assert.Equal(["PT"], filters.Places.Countries!);
        Assert.Equal(["HD da Ana"], filters.Sources!);
        Assert.False(filters.UndatedOnly);
        Assert.Equal((SearchSort.CapturedAtDesc, 200), (query.Sort, query.Limit));
    }

    [Fact]
    public void Defaults_to_everything_newest_first_200_at_a_time_when_empty()
    {
        Assert.True(SearchQueryJson.TryParse("{}", out var query, out _));

        Assert.True(query.Filters.IsEmpty);
        Assert.Equal((SearchSort.CapturedAtDesc, SearchQuery.DefaultLimit), (query.Sort, query.Limit));
    }

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { """{"filter": {}}""", "filter" },
        { """{"filters": {"people": ["Ana"]}}""", "people" },
        { """{"filters": {"places": {"region": "Porto"}}}""", "region" },
        { """{"filters": {"onThisDay": {"days": 1}}}""", "days" },
        { """{"filters": {"months": [13]}}""", "filters.months" },
        { """{"filters": {"months": [0]}}""", "filters.months" },
        { """{"filters": {"kinds": ["audio"]}}""", "filters.kinds" },
        { """{"filters": {"kinds": []}}""", "filters.kinds" },
        { """{"filters": {"years": [1700]}}""", "filters.years" },
        { """{"filters": {"years": ["2019"]}}""", "years" },
        { """{"filters": {"years": [2019.5]}}""", "years" },
        { """{"filters": {"dateFrom": "2019-13-01"}}""", "dateFrom" },
        { """{"filters": {"dateFrom": "2020-01-01", "dateTo": "2019-12-31"}}""", "dateFrom" },
        { """{"filters": {"onThisDay": {"yearsAgo": -1}}}""", "yearsAgo" },
        { """{"filters": {"places": {"countries": ["pt"]}}}""", "countries" },
        { """{"filters": {"places": {"cities": [" "]}}}""", "cities" },
        { """{"filters": {"places": {"cities": [null]}}}""", "cities" },
        { """{"filters": {"sources": ["HD", ""]}}""", "sources" },
        { """{"filters": {"undatedOnly": true, "years": [2019]}}""", "undatedOnly" },
        { """{"filters": null}""", "filters" },
        { """{"sort": "random"}""", "sort" },
        { """{"limit": 0}""", "limit" },
        { """{"limit": 1001}""", "limit" },
        { """{"limit": 10, "limit": 20}""", "limit" },
        { """{"filters": {"months": [12]}""", "JSON" },
        { "[]", "JSON" },
        { "", "JSON" },
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public void Rejects_a_query_with_a_message_when_a_field_is_unknown_or_out_of_range(string json, string mentioned)
    {
        Assert.False(SearchQueryJson.TryParse(json, out _, out var error));

        Assert.Contains(mentioned, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Survives_the_url_unchanged_when_encoded_and_decoded()
    {
        Assert.True(SearchQueryJson.TryParse(StoryExample, out var query, out _));

        var encoded = SearchQueryJson.Encode(query);
        Assert.True(SearchQueryJson.TryDecode(encoded, out var decoded, out var error), error);

        Assert.Equal(query, decoded);
        Assert.Equal(SearchQueryJson.Serialize(query), SearchQueryJson.Serialize(decoded));
        // base64url: safe in a query string as it is.
        Assert.Matches("^[A-Za-z0-9_-]+$", encoded);
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("_w")]
    [InlineData("")]
    public void Rejects_an_address_that_is_not_a_base64url_query(string encoded)
    {
        // "_w" decodes to a byte that is not UTF-8.
        Assert.False(SearchQueryJson.TryDecode(encoded, out _, out _));
    }

    [Fact]
    public void Rejects_an_address_longer_than_the_limit()
    {
        var huge = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(' ', SearchQueryJson.MaxEncodedLength))).TrimEnd('=');

        Assert.False(SearchQueryJson.TryDecode(huge, out _, out _));
    }

    public static TheoryData<string> SchemaExamples() =>
    [
        StoryExample,
        "{}",
        """{"filters": {"undatedOnly": true, "kinds": ["video"]}, "sort": "capturedAtAsc", "limit": 1}""",
        """{"filters": {"onThisDay": {}}}""",
        """{"filter": {}}""",
        """{"filters": {"people": ["Ana"]}}""",
        """{"filters": {"places": {"region": "Porto"}}}""",
        """{"filters": {"months": [13]}}""",
        """{"filters": {"kinds": ["audio"]}}""",
        """{"filters": {"kinds": []}}""",
        """{"filters": {"years": [1700]}}""",
        """{"filters": {"years": ["2019"]}}""",
        """{"filters": {"onThisDay": {"yearsAgo": -1}}}""",
        """{"filters": {"places": {"countries": ["pt"]}}}""",
        """{"filters": {"places": {"cities": [" "]}}}""",
        """{"filters": {"sources": ["HD", ""]}}""",
        """{"filters": null}""",
        """{"sort": "random"}""",
        """{"limit": 1001}""",
        "[]",
    ];

    [Theory]
    [MemberData(nameof(SchemaExamples))]
    public void Agrees_with_docs_schemas_search_v1_json_on_what_is_valid(string json)
    {
        // The rules the schema cannot say (dateFrom before dateTo, undatedOnly alone, real calendar days, duplicate
        // keys) are left out of this comparison and tested above.
        var schemaSaysValid = s_schema.Value.Evaluate(JsonNode.Parse(json)).IsValid;

        Assert.Equal(schemaSaysValid, SearchQueryJson.TryParse(json, out _, out _));
    }
}
