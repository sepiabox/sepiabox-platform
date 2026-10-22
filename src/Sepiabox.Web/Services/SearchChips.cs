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

        if (f.SavedPlaces is { } savedPlaces)
        {
            Add(text["Chip_SavedPlaces", string.Join(", ", savedPlaces)], f with { SavedPlaces = null });
        }

        if (f.DistanceFrom is { } distance)
        {
            Add(Distance(text, distance), f with { DistanceFrom = null });
        }

        if (f.Sources is { } sources)
        {
            Add(text["Chip_Sources", string.Join(", ", sources)], f with { Sources = null });
        }

        if (f.SourceCount is { } sourceCount)
        {
            Add(SourceCount(text, sourceCount), f with { SourceCount = null });
        }

        if (f.People is { } people)
        {
            if (people.All is { } names)
            {
                Add(text["Chip_People", string.Join(", ", names)], f with { People = PeopleOrNone(people with { All = null }) });
            }

            if (people.IncludeMe)
            {
                Add(text["Chip_Me"], f with { People = PeopleOrNone(people with { IncludeMe = false }) });
            }
        }

        if (f.EventIds is { } eventIds)
        {
            // The ids mean nothing to a reader, and a chip is no place to look up titles: how many occasions it is.
            Add(eventIds.Count == 1 ? text["Chip_Occasion_One"].Value : text["Chip_Occasion_Other", eventIds.Count].Value, f with { EventIds = null });
        }

        if (f.Visual is { } visual)
        {
            Add(text["Chip_Visual", visual.Text], f with { Visual = null });
        }

        if (f.SimilarTo is { } similar)
        {
            // "Show more" raised the query's limit with the similar one's (SimilarSearch.More): without the search for
            // similar ones, that raise goes too, or the timeline would show a "First 1000" it never asked for.
            var raised = similar.Limit is { } asked && asked > SearchQuery.DefaultLimit && query.Limit == asked;
            chips.Add(new SearchChip(
                text["Chip_SimilarTo"],
                query with { Filters = f with { SimilarTo = null }, Limit = raised ? SearchQuery.DefaultLimit : query.Limit }));
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
            var size = aggregate.Sum is not null;
            var label = aggregate.GroupBy is { } groupBy
                ? text[size ? "Chip_SizeBy" : "Chip_CountBy", text["Group_" + char.ToUpperInvariant(groupBy[0]) + groupBy[1..]]]
                : text[size ? "Chip_Size" : "Chip_Count"];
            chips.Add(new SearchChip(label, query with { Aggregate = null }));
        }

        // A search for similar ones raises its limit with "Show more" (S-017): how many it shows is not a filter to remove.
        if (query.Aggregate is null && query.Limit != SearchQuery.DefaultLimit && f.SimilarTo is null)
        {
            chips.Add(new SearchChip(text["Chip_Limit", query.Limit], query with { Limit = SearchQuery.DefaultLimit }));
        }

        return chips;
    }

    // "In only one source", "In 3 sources", "In 2 or more sources".
    private static string SourceCount(IStringLocalizer<SharedResource> text, SourceCountFilter count) =>
        count switch
        {
            { Eq: 1 } => text["Chip_SourceOne"],
            { Eq: { } eq } => text["Chip_SourceEq", eq],
            { Gte: { } gte } => text["Chip_SourceAtLeast", gte],
            _ => string.Empty,
        };

    // "More than 100 km from home", "Up to 5 km from Beach house", "100–500 km from home".
    private static string Distance(IStringLocalizer<SharedResource> text, DistanceFilter distance)
    {
        var from = distance.Home ? text["Chip_Home"].Value : distance.Place ?? string.Empty;
        string Km(double km) => km.ToString("0.##", CultureInfo.CurrentCulture);
        return (distance.MinKm, distance.MaxKm) switch
        {
            ({ } min, { } max) => text["Chip_DistanceBetween", Km(min), Km(max), from],
            ({ } min, null) => text["Chip_DistanceMin", Km(min), from],
            (null, { } max) => text["Chip_DistanceMax", Km(max), from],
            _ => from,
        };
    }

    // A people filter with no names and no "me" is no filter at all.
    private static PeopleFilter? PeopleOrNone(PeopleFilter people) => people.All is null && !people.IncludeMe ? null : people;

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
