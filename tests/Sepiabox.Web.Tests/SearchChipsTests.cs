using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The chips that show what a search applies (S-025), in the UI culture.</summary>
public sealed class SearchChipsTests
{
    private static readonly SearchQuery Everything = new()
    {
        Filters = new SearchFilters
        {
            Kinds = ["photo", "video"],
            DateFrom = new DateOnly(2019, 12, 1),
            DateTo = new DateOnly(2019, 12, 31),
            Years = [2018, 2019],
            Months = [7, 12],
            Places = new PlaceFilter { Cities = ["Lisbon"], Countries = ["BR"] },
            Sources = ["HD da Ana"],
            Visual = new VisualFilter { Text = "snow" },
        },
        Sort = SearchSort.CapturedAtAsc,
        Limit = 10,
    };

    [Theory]
    [InlineData(Cultures.English, new[]
    {
        "Photos, Videos", "Year: 2018, 2019", "Month: July, December", "From December 1, 2019", "Until December 31, 2019",
        "City: Lisbon", "Country: Brazil", "Source: HD da Ana", "Showing: snow", "Oldest first", "First 10",
    })]
    [InlineData(Cultures.BrazilianPortuguese, new[]
    {
        "Fotos, Vídeos", "Ano: 2018, 2019", "Mês: Julho, Dezembro", "A partir de 1 de dezembro de 2019", "Até 31 de dezembro de 2019",
        "Cidade: Lisbon", "País: Brazil", "Origem: HD da Ana", "Mostrando: snow", "Mais antigas primeiro", "Primeiros 10",
    })]
    public void Labels_every_part_of_the_search_in_the_ui_culture(string culture, string[] expected)
    {
        using var _ = Cultures.Use(culture);

        var chips = SearchChips.For(Everything, Cultures.Text());

        Assert.Equal(expected, chips.Select(c => c.Label));
    }

    [Theory]
    [InlineData(Cultures.English, "On this day", "This day, 3 years ago", "Without a date")]
    [InlineData(Cultures.BrazilianPortuguese, "Neste dia", "Neste dia, há 3 anos", "Sem data")]
    public void Labels_on_this_day_and_undated(string culture, string anyYear, string yearsAgo, string undated)
    {
        using var _ = Cultures.Use(culture);
        var text = Cultures.Text();

        Assert.Equal(anyYear, Single(new SearchFilters { OnThisDay = new OnThisDayFilter() }, text));
        Assert.Equal(yearsAgo, Single(new SearchFilters { OnThisDay = new OnThisDayFilter { YearsAgo = 3 } }, text));
        Assert.Equal(undated, Single(new SearchFilters { UndatedOnly = true }, text));
    }

    [Fact]
    public void Each_chip_removes_only_its_own_part()
    {
        var chips = SearchChips.For(Everything, Cultures.Text());

        var withoutCity = chips.Single(c => c.Label == "City: Lisbon").Without;
        var withoutCountry = chips.Single(c => c.Label == "Country: Brazil").Without;
        var withoutOrder = chips.Single(c => c.Label == "Oldest first").Without;

        Assert.Equal(Everything with { Filters = Everything.Filters with { Places = new PlaceFilter { Countries = ["BR"] } } }, withoutCity);
        Assert.Equal(Everything with { Filters = Everything.Filters with { Places = new PlaceFilter { Cities = ["Lisbon"] } } }, withoutCountry);
        Assert.Equal(Everything with { Sort = SearchSort.CapturedAtDesc }, withoutOrder);
        Assert.All(chips, c => Assert.Null(SearchQueryJson.Validate(c.Without)));
    }

    [Fact]
    public void Removing_the_only_place_list_removes_the_place_filter()
    {
        var query = new SearchQuery { Filters = new SearchFilters { Places = new PlaceFilter { Cities = ["Porto"] } } };

        var chip = Assert.Single(SearchChips.For(query, Cultures.Text()));

        Assert.Null(chip.Without.Filters.Places);
        Assert.True(chip.Without.Filters.IsEmpty);
    }

    [Fact]
    public void A_search_with_no_filters_has_no_chips() =>
        Assert.Empty(SearchChips.For(new SearchQuery(), Cultures.Text()));

    [Theory]
    [InlineData(Cultures.English, "Similar to a photo")]
    [InlineData(Cultures.BrazilianPortuguese, "Parecidas com uma foto")]
    public void Labels_a_search_for_similar_photos_and_removes_only_it(string culture, string expected)
    {
        using var _ = Cultures.Use(culture);
        var query = SearchLinks.Similar(Ids.Of(1)) with { Filters = SearchLinks.Similar(Ids.Of(1)).Filters with { Years = [2020] } };

        var chip = SearchChips.For(query, Cultures.Text()).Single(c => c.Label == expected);

        Assert.Equal(new SearchQuery { Filters = new SearchFilters { Years = [2020] } }, chip.Without);
    }

    [Fact]
    public void Removing_similar_photos_after_show_more_also_drops_the_limit_it_raised_but_not_one_set_apart()
    {
        var shown = SimilarSearch.More(SimilarSearch.More(SearchLinks.Similar(Ids.Of(1)))!)!;
        var setApart = SearchLinks.Similar(Ids.Of(1)) with { Limit = 500 };

        var withoutShown = SearchChips.For(shown, Cultures.Text()).Single(c => c.Label == "Similar to a photo").Without;
        var withoutSetApart = SearchChips.For(setApart, Cultures.Text()).Single(c => c.Label == "Similar to a photo").Without;

        Assert.Equal(1000, shown.Limit);
        Assert.Equal(new SearchQuery(), withoutShown);
        Assert.DoesNotContain(SearchChips.For(withoutShown, Cultures.Text()), c => c.Label.StartsWith("First", StringComparison.Ordinal));
        // A limit the similar search did not raise is the query's own, and stays.
        Assert.Equal(new SearchQuery { Limit = 500 }, withoutSetApart);
    }

    [Fact]
    public void Refuses_a_translated_answer_with_similar_to_since_a_question_cannot_name_an_asset()
    {
        var json = $$$$"""{"filters": {"similarTo": {"assetId": "{{{{Ids.Of(1)}}}}"}}}""";

        Assert.True(SearchQueryJson.TryParse(json, out _, out _));
        Assert.Contains("similarTo", QuestionService.Validate(json), StringComparison.Ordinal);
        Assert.Null(QuestionService.Validate("""{"filters": {"years": [2020]}}"""));
    }

    private static string Single(SearchFilters filters, Microsoft.Extensions.Localization.IStringLocalizer<SharedResource> text) =>
        Assert.Single(SearchChips.For(new SearchQuery { Filters = filters }, text)).Label;
}
