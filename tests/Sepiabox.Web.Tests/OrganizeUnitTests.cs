using Sepiabox.Catalog.Organization;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>Bursts, and the blurry and hidden filters in a query, its chips and its links (S-027, D-098).</summary>
public sealed class OrganizeUnitTests
{
    private const string Hash = "0000000000000000";
    private static readonly DateTime s_start = new(2025, 3, 1, 12, 0, 0);

    [Fact]
    public void Three_photos_a_second_apart_that_look_alike_form_one_burst()
    {
        var photos = new[] { Burst(1, 0, Hash), Burst(2, 1, "0000000000000003"), Burst(3, 2, "0000000000000007") };

        var bursts = BurstFinder.Find(photos);

        Assert.Equal([Ids.Of(1), Ids.Of(2), Ids.Of(3)], Assert.Single(bursts).Select(p => p.Id));
    }

    [Fact]
    public void Starts_another_burst_after_a_gap_of_more_than_two_seconds()
    {
        var bursts = BurstFinder.Find([Burst(1, 0, Hash), Burst(2, 2, Hash), Burst(3, 4.5, Hash), Burst(4, 5, Hash)]);

        Assert.Equal([[Ids.Of(1), Ids.Of(2)], [Ids.Of(3), Ids.Of(4)]], bursts.Select(b => b.Select(p => p.Id).ToArray()));
    }

    [Fact]
    public void Cuts_a_long_run_into_bursts_of_at_most_twelve_photos_and_drops_a_leftover_single_photo()
    {
        var run = Enumerable.Range(1, 25).Select(n => Burst(n, n - 1, Hash)).ToList();

        var bursts = BurstFinder.Find(run);

        Assert.Equal([12, 12], bursts.Select(b => b.Count));
        Assert.Equal(BurstFinder.MaxPhotos, bursts[0].Count);
        Assert.Equal(Ids.Of(13), bursts[1][0].Id);
        Assert.Equal([3], BurstFinder.Find(Enumerable.Range(1, 15).Select(n => Burst(n, n - 1, Hash))).Skip(1).Select(b => b.Count));
    }

    [Fact]
    public void Leaves_out_photos_that_do_not_look_alike_and_single_photos()
    {
        var bursts = BurstFinder.Find([Burst(1, 0, Hash), Burst(2, 1, "ffffffffffffffff"), Burst(3, 2, Hash)]);

        Assert.Empty(bursts);
    }

    [Fact]
    public void Accepts_exactly_ten_bits_apart_and_refuses_eleven()
    {
        Assert.Single(BurstFinder.Find([Burst(1, 0, Hash), Burst(2, 1, "00000000000003ff")]));
        Assert.Empty(BurstFinder.Find([Burst(1, 0, Hash), Burst(2, 1, "00000000000007ff")]));
    }

    [Fact]
    public void Finds_the_same_bursts_whatever_the_order_the_photos_come_in()
    {
        var photos = new[] { Burst(3, 2, Hash), Burst(1, 0, Hash), Burst(2, 1, Hash), Burst(9, 100, Hash) };

        Assert.Equal(
            BurstFinder.Find(photos.OrderBy(p => p.Id, StringComparer.Ordinal)).Select(b => b.Select(p => p.Id).ToArray()),
            BurstFinder.Find(photos).Select(b => b.Select(p => p.Id).ToArray()));
    }

    [Fact]
    public void Keeps_the_sharpest_photo_of_a_burst_and_the_earliest_when_equal()
    {
        var burst = BurstFinder.Find([Burst(1, 0, Hash, 50), Burst(2, 1, Hash, 90), Burst(3, 2, Hash, 90)])[0];

        Assert.Equal(Ids.Of(2), BurstFinder.Sharpest(burst).Id);
    }

    [Theory]
    [InlineData("""{"filters": {"quality": {"blurry": true}}}""", true)]
    [InlineData("""{"filters": {"quality": {"blurry": false}}}""", false)]
    public void Parses_the_quality_filter(string json, bool blurry)
    {
        Assert.True(SearchQueryJson.TryParse(json, out var query, out var error), error);

        Assert.Equal(blurry, query.Filters.Quality!.Blurry);
        Assert.True(SearchQueryJson.TryDecode(SearchQueryJson.Encode(query), out var decoded, out _));
        Assert.Equal(query, decoded);
    }

    [Theory]
    [InlineData("""{"filters": {"quality": {}}}""", "quality")]
    [InlineData("""{"filters": {"quality": {"blurry": "yes"}}}""", "quality")]
    [InlineData("""{"filters": {"quality": {"blurry": true, "minSharpness": 3}}}""", "quality")]
    [InlineData("""{"filters": {"includeHidden": "yes"}}""", "includeHidden")]
    public void Refuses_a_malformed_quality_or_include_hidden(string json, string field)
    {
        Assert.False(SearchQueryJson.TryParse(json, out _, out var error));
        Assert.Contains(field, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_include_hidden_and_keeps_it_through_the_url()
    {
        Assert.True(SearchQueryJson.TryParse("""{"filters": {"includeHidden": true}}""", out var query, out _));

        Assert.True(query.Filters.IncludeHidden);
        Assert.False(query.Filters.IsEmpty);
        Assert.True(SearchQueryJson.TryDecode(SearchQueryJson.Encode(query), out var decoded, out _));
        Assert.Equal(query, decoded);
        Assert.NotEqual(query, query with { Filters = query.Filters with { IncludeHidden = false } });
    }

    [Fact]
    public void Questions_never_name_the_quality_or_what_was_set_aside()
    {
        Assert.Contains("quality", QuestionService.Validate("""{"filters": {"quality": {"blurry": true}}}"""), StringComparison.Ordinal);
        Assert.Contains("includeHidden", QuestionService.Validate("""{"filters": {"includeHidden": true}}"""), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Cultures.English, "Blurry photos", "Sharp photos", "Including set aside")]
    [InlineData(Cultures.BrazilianPortuguese, "Fotos desfocadas", "Fotos nítidas", "Incluindo ocultas")]
    public void Labels_the_quality_and_hidden_chips_and_each_removes_only_itself(string culture, string blurry, string sharp, string hidden)
    {
        using var _ = Cultures.Use(culture);
        var query = new SearchQuery { Filters = new SearchFilters { Years = [2019], Quality = new QualityFilter { Blurry = true }, IncludeHidden = true } };

        var chips = SearchChips.For(query, Cultures.Text());

        Assert.Contains(chips, c => c.Label == blurry);
        Assert.Contains(chips, c => c.Label == hidden);
        Assert.Equal(query with { Filters = query.Filters with { Quality = null } }, chips.Single(c => c.Label == blurry).Without);
        Assert.Equal(query with { Filters = query.Filters with { IncludeHidden = false } }, chips.Single(c => c.Label == hidden).Without);
        var sharpChips = SearchChips.For(query with { Filters = query.Filters with { Quality = new QualityFilter { Blurry = false } } }, Cultures.Text());
        Assert.Contains(sharpChips, c => c.Label == sharp);
    }

    [Fact]
    public void Carries_the_quality_and_hidden_fields_through_the_filter_bar()
    {
        var query = SearchLinks.FromFields("2019", null, null, null, null, blurry: "1", includeHidden: "1");

        Assert.True(query.Filters.Quality!.Blurry);
        Assert.True(query.Filters.IncludeHidden);
        Assert.False(SearchLinks.FromFields(null, null, null, null, null, blurry: "0").Filters.Quality!.Blurry);
        Assert.True(SearchLinks.FromFields(null, null, null, null, null, blurry: "maybe", includeHidden: "0").Filters.IsEmpty);
        Assert.Null(SearchQueryJson.Validate(query));
    }

    private static BurstPhoto Burst(int n, double seconds, string hash, double sharpness = 100) =>
        new(Ids.Of(n), s_start.AddSeconds(seconds), hash, sharpness);
}
