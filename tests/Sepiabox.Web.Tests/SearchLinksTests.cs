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
}
