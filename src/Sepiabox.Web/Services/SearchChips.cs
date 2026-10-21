using System.Globalization;
using Microsoft.Extensions.Localization;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

/// <summary>One part of a search, as a chip: its label in the current culture, and the search without it.</summary>
public sealed record SearchChip(string Label, SearchQuery Without);

/// <summary>
/// The filters a search applies, as chips (S-025): what a question was understood as, or what the filter bar set,
/// each with a link to the same search without it. Labels are in the UI culture (D-050), whatever language the
/// question was asked in.
/// </summary>
public static class SearchChips
{
    public static IReadOnlyList<SearchChip> For(SearchQuery query, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(text);
        var f = query.Filters;
        var chips = new List<SearchChip>();

        void Add(string label, SearchFilters without) => chips.Add(new SearchChip(label, query with { Filters = without }));

        if (f.Kinds is { } kinds)
        {
            Add(string.Join(", ", kinds.Select(k => AssetKinds.IsVideo(k) ? text["Filter_Videos"].Value : text["Filter_Photos"].Value)), f with { Kinds = null });
        }

        if (f.Years is { } years)
        {
            Add(text["Chip_Years", string.Join(", ", years.Select(y => y.ToString(CultureInfo.InvariantCulture)))], f with { Years = null });
        }

        if (f.Months is { } months)
        {
            Add(text["Chip_Months", string.Join(", ", months.Select(MonthName))], f with { Months = null });
        }

        if (f.DateFrom is { } from)
        {
            Add(text["Chip_From", Day(text, from)], f with { DateFrom = null });
        }

        if (f.DateTo is { } to)
        {
            Add(text["Chip_To", Day(text, to)], f with { DateTo = null });
        }

        if (f.OnThisDay is { } onThisDay)
        {
            Add(onThisDay.YearsAgo is { } ago ? text["Chip_YearsAgo", ago] : text["Filter_OnThisDay"], f with { OnThisDay = null });
        }

        if (f.Places?.Cities is { } cities)
        {
            Add(text["Chip_Cities", string.Join(", ", cities)], f with { Places = Places(f.Places with { Cities = null }) });
        }

        if (f.Places?.Countries is { } countries)
        {
            Add(text["Chip_Countries", string.Join(", ", countries.Select(Country))], f with { Places = Places(f.Places with { Countries = null }) });
        }

        if (f.Sources is { } sources)
        {
            Add(text["Chip_Sources", string.Join(", ", sources)], f with { Sources = null });
        }

        if (f.Visual is { } visual)
        {
            Add(text["Chip_Visual", visual.Text], f with { Visual = null });
        }

        if (f.UndatedOnly)
        {
            Add(text["Chip_Undated"], f with { UndatedOnly = false });
        }

        if (query.Aggregate is null && query.Sort == SearchSort.CapturedAtAsc)
        {
            chips.Add(new SearchChip(text["Chip_OldestFirst"], query with { Sort = SearchSort.CapturedAtDesc }));
        }

        if (query.Aggregate is { } aggregate)
        {
            var label = aggregate.GroupBy is { } groupBy
                ? text["Chip_CountBy", text["Group_" + char.ToUpperInvariant(groupBy[0]) + groupBy[1..]]]
                : text["Chip_Count"];
            chips.Add(new SearchChip(label, query with { Aggregate = null }));
        }

        if (query.Aggregate is null && query.Limit != SearchQuery.DefaultLimit)
        {
            chips.Add(new SearchChip(text["Chip_Limit", query.Limit], query with { Limit = SearchQuery.DefaultLimit }));
        }

        return chips;
    }

    // A place filter with neither list is no filter at all.
    private static PlaceFilter? Places(PlaceFilter places) => places.Cities is null && places.Countries is null ? null : places;

    private static string Day(IStringLocalizer<SharedResource> text, DateOnly day) =>
        day.ToString(text["Format_Day"], CultureInfo.CurrentCulture);

    private static string MonthName(int month)
    {
        var name = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
        return name.Length == 0 ? name : CultureInfo.CurrentCulture.TextInfo.ToUpper(name[0]) + name[1..];
    }

    // The country's English name, as the catalog stores places (GeoNames' names, D-066), like the cities next to it.
    // Not RegionInfo.DisplayName: .NET caches it in the first UI culture that asked, so it would mix languages.
    private static string Country(string code)
    {
        try
        {
            return new RegionInfo(code).EnglishName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }
}
