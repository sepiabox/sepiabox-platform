using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class SearchLinksTests
{
    [Fact]
    public void Turns_the_filter_bar_s_fields_into_one_filter_each_when_set()
    {
        var query = SearchLinks.FromFields("2019", "12", "photo", "Lisbon", "HD da Ana");

        Assert.Equal(
            new SearchQuery
            {
                Filters = new SearchFilters
                {
                    Years = [2019],
                    Months = [12],
                    Kinds = ["photo"],
                    Places = new PlaceFilter { Cities = ["Lisbon"] },
                    Sources = ["HD da Ana"],
                },
            },
            query);
    }

    [Theory]
    [InlineData("", "", "", "", "")]
    [InlineData(null, null, null, null, null)]
    [InlineData("abc", "13", "audio", "  ", "")]
    [InlineData("99999", "-1", "PHOTO", null, null)]
    public void Leaves_out_fields_that_are_empty_or_not_a_valid_value(string? year, string? month, string? kind, string? city, string? source)
    {
        var query = SearchLinks.FromFields(year, month, kind, city, source);

        Assert.True(query.Filters.IsEmpty);
        Assert.Equal("./", SearchLinks.Timeline(query));
    }

    [Fact]
    public void Gives_the_same_filters_back_when_the_link_is_opened_again()
    {
        var chosen = SearchLinks.FromFields("2019", "12", "video", "Lisbon", null);

        var link = SearchLinks.Timeline(chosen);
        Assert.StartsWith("./?q=", link, StringComparison.Ordinal);
        Assert.True(SearchQueryJson.TryDecode(link["./?q=".Length..], out var opened, out _));

        Assert.Equal(chosen, opened);
        Assert.Equal(new FilterFields(2019, 12, "video", "Lisbon", null), SearchLinks.ToFields(opened));
    }

    [Theory]
    [InlineData("  beach at sunset ", "beach at sunset")]
    [InlineData("snow\u0000", "snow")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Turns_the_description_box_into_a_visual_filter_when_it_has_text(string? typed, string? expected)
    {
        var query = SearchLinks.FromFields(null, null, null, null, null, typed);

        Assert.Equal(expected, query.Filters.Visual?.Text);
        Assert.Equal(expected, SearchLinks.ToFields(query).Text);
        Assert.Null(SearchQueryJson.Validate(query));
    }

    [Fact]
    public void Cuts_a_description_longer_than_the_dsl_allows()
    {
        var query = SearchLinks.FromFields(null, null, null, null, null, new string('a', 1000));

        Assert.Equal(SearchQueryJson.MaxTextLength, query.Filters.Visual!.Text.Length);
        Assert.Null(SearchQueryJson.Validate(query));
    }

    [Fact]
    public void Shows_only_single_values_in_the_bar_when_the_query_has_several()
    {
        var query = new SearchQuery { Filters = new SearchFilters { Years = [2019, 2020], Months = [12] } };

        Assert.Equal(new FilterFields(null, 12, null, null, null), SearchLinks.ToFields(query));
    }

    [Fact]
    public void On_this_day_is_today_s_day_in_every_earlier_year()
    {
        Assert.NotNull(SearchLinks.OnThisDay.Filters.OnThisDay);
        Assert.Null(SearchLinks.OnThisDay.Filters.OnThisDay!.YearsAgo);
        Assert.Null(SearchQueryJson.Validate(SearchLinks.OnThisDay));
    }

    [Fact]
    public void Keeps_the_photo_whose_similar_ones_the_bar_narrows_with_the_number_asked_for()
    {
        var id = Ids.Of(7);

        var narrowed = SearchLinks.FromFields("2020", null, null, null, null, similar: id, similarLimit: "200");
        var raised = SearchLinks.FromFields(null, null, null, null, null, similar: id, similarLimit: "1000");

        Assert.Equal(
            new SearchQuery { Filters = new SearchFilters { Years = [2020], SimilarTo = new SimilarToFilter { AssetId = id, Limit = 200 } } },
            narrowed);
        // A raised number raises the query's own limit, which would otherwise cap it.
        Assert.Equal((1000, 1000), (raised.Limit, raised.ResultLimit));
        Assert.Null(SearchQueryJson.Validate(raised));
        // The bar shows the same search back, with the photo in its hidden fields.
        Assert.Equal(new SimilarToFilter { AssetId = id, Limit = 200 }, SearchLinks.ToFields(narrowed).SimilarTo);
    }

    [Theory]
    [InlineData("not an id", null)]
    [InlineData("ABCDEF", null)]
    [InlineData("", null)]
    public void Ignores_a_hidden_similar_field_that_is_not_an_asset_id(string similar, string? limit)
    {
        Assert.Null(SearchLinks.FromFields("2020", null, null, null, null, similar: similar, similarLimit: limit).Filters.SimilarTo);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("abc")]
    public void Uses_the_default_number_when_the_hidden_one_is_out_of_range(string limit)
    {
        var query = SearchLinks.FromFields(null, null, null, null, null, similar: Ids.Of(7), similarLimit: limit);

        Assert.Null(query.Filters.SimilarTo!.Limit);
        Assert.Equal(SimilarToFilter.DefaultLimit, query.ResultLimit);
    }

    [Fact]
    public void A_description_typed_in_the_bar_replaces_the_search_for_similar_ones()
    {
        var query = SearchLinks.FromFields(null, null, null, null, null, text: "snow", similar: Ids.Of(7));

        Assert.Null(query.Filters.SimilarTo);
        Assert.Equal("snow", query.Filters.Visual!.Text);
        Assert.Null(SearchQueryJson.Validate(query));
    }

    [Fact]
    public void Shows_more_similar_ones_in_steps_of_50_200_and_1000_then_no_more()
    {
        var first = SearchLinks.Similar(Ids.Of(7)) with { Filters = SearchLinks.Similar(Ids.Of(7)).Filters with { Years = [2020] } };

        var second = SimilarSearch.More(first)!;
        var third = SimilarSearch.More(second)!;

        Assert.Equal((200, 200), (second.ResultLimit, second.Limit));
        Assert.Equal([2020], second.Filters.Years!);
        Assert.Equal((1000, 1000), (third.ResultLimit, third.Limit));
        Assert.Null(SimilarSearch.More(third));
        Assert.Null(SimilarSearch.More(new SearchQuery()));
        Assert.All(new[] { second, third }, q => Assert.Null(SearchQueryJson.Validate(q)));
    }
}
