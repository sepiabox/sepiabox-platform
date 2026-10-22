using System.Globalization;
using Microsoft.Extensions.Localization;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Localization;

namespace Sepiabox.Web.Services;

/// <summary>One row of a ranking: its label in the current culture, its count, and the search for its items (null when none).</summary>
public sealed record CountRow(string Label, int Count, SearchQuery? Items);

/// <summary>
/// The words of a counting answer (S-026) in the current culture (D-050): "312 videos in 2019" in English,
/// the heading of a ranking, its rows' labels and links. Plurals come from the resources (one, other); the other
/// filters are shown by the chips next to the card.
/// </summary>
public static class CountAnswers
{
    /// <summary>The sentence under the big number: the number with its noun, and the year, place or day it is about.</summary>
    public static string Sentence(SearchQuery query, int count, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(text);
        var events = query.Aggregate?.Count == SearchCount.Events;
        var noun = (events ? "Count_Events" : Noun(query.Filters)) + (count == 1 ? "_One" : "_Other");
        var ofType = events && query.Filters.EventTypes is { } types
            ? text["Count_OfType", string.Join(", ", types.Select(t => text.EventTypeLabel(t)))].Value
            : string.Empty;
        return text[noun, count.ToString("N0", CultureInfo.CurrentCulture)] + ofType + About(query.Filters, text);
    }

    /// <summary>The heading of a ranking: "Photos by year in Lisbon".</summary>
    public static string Heading(SearchQuery query, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(text);
        if (query.Aggregate?.GroupBy == SearchGroupBy.EventType)
        {
            return text["Count_ByEventType"] + About(query.Filters, text);
        }

        var noun = Noun(query.Filters) switch
        {
            "Count_Photos" => text["Filter_Photos"].Value,
            "Count_Videos" => text["Filter_Videos"].Value,
            _ => text["Count_ItemsNoun"].Value,
        };
        var key = query.Aggregate?.GroupBy switch
        {
            SearchGroupBy.Year => "Count_ByYear",
            SearchGroupBy.Month => "Count_ByMonth",
            SearchGroupBy.Kind => "Count_ByKind",
            SearchGroupBy.City => "Count_ByCity",
            SearchGroupBy.Person => "Count_ByPerson",
            _ => "Count_BySource",
        };
        return text[key, noun] + About(query.Filters, text);
    }

    /// <summary>The ranking's rows, largest first as counted, each linking to its items where a filter can say so.</summary>
    public static IReadOnlyList<CountRow> Rows(SearchQuery query, CountResult count, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(count);
        ArgumentNullException.ThrowIfNull(text);
        var list = query with { Aggregate = null };
        var f = list.Filters;
        return [.. count.Groups.Select(g => query.Aggregate?.GroupBy switch
        {
            SearchGroupBy.Year when Number(g.Key) is { } year => new CountRow(g.Key!, g.Count, list with { Filters = f with { Years = [year] } }),
            SearchGroupBy.Month when Number(g.Key) is { } month => new CountRow(MonthName(month), g.Count, list with { Filters = f with { Months = [month] } }),
            // Each type's photos; the occasions without a type have no filter to say so.
            SearchGroupBy.EventType when g.Key is not null => new CountRow(
                text.EventTypeLabel(g.Key), g.Count, list with { Filters = f with { EventTypes = [g.Key] } }),
            SearchGroupBy.EventType => new CountRow(text["Count_NoType"], g.Count, null),
            SearchGroupBy.Year or SearchGroupBy.Month => new CountRow(text["NoDate"], g.Count, f.HasTimeFilter ? null : list with { Filters = f.UndatedInstead }),
            SearchGroupBy.Kind => new CountRow(
                AssetKinds.IsVideo(g.Key ?? string.Empty) ? text["Filter_Videos"] : text["Filter_Photos"], g.Count, list with { Filters = f with { Kinds = [g.Key!] } }),
            SearchGroupBy.City when g.Key is not null => new CountRow(g.Key, g.Count, list with { Filters = f with { Places = new PlaceFilter { Cities = [g.Key] } } }),
            SearchGroupBy.City => new CountRow(text["Count_NoPlace"], g.Count, null),
            // Each person's photos, with the people already asked for (S-021).
            SearchGroupBy.Person => new CountRow(g.Key ?? string.Empty, g.Count, list with
            {
                Filters = f with { People = (f.People ?? new PeopleFilter()) with { All = [.. f.People?.All ?? [], g.Key!] } },
            }),
            _ => new CountRow(g.Key ?? string.Empty, g.Count, list with { Filters = f with { Sources = [g.Key!] } }),
        })];
    }

    /// <summary>"3 items have no date and cannot match the dates asked."</summary>
    public static string Undated(int count, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return count == 1 ? text["Count_Undated_One"] : text["Count_Undated_Other", count.ToString("N0", CultureInfo.CurrentCulture)];
    }

    private static string Noun(SearchFilters filters) => filters.Kinds switch
    {
        [var only] when only == AssetKinds.Photo => "Count_Photos",
        [var only] when only == AssetKinds.Video => "Count_Videos",
        _ => "Count_Items",
    };

    // The parts of the question a sentence can say simply; the chips show all of them.
    private static string About(SearchFilters filters, IStringLocalizer<SharedResource> text)
    {
        var about = string.Empty;
        if (filters is { Years: [var year], Months: [var month] })
        {
            about += text["Count_InYear", text.MonthAndYear(new DateOnly(year, month, 1))];
        }
        else if (filters is { Years: [var only], Months: null })
        {
            about += text["Count_InYear", only.ToString(CultureInfo.InvariantCulture)];
        }

        if (filters.Places is { Cities: [var city], Countries: null })
        {
            about += text["Count_InPlace", city];
        }

        if (filters.Sources is [var source])
        {
            about += text["Count_FromSource", source];
        }

        if (filters.People?.All is { } people)
        {
            about += text["Count_WithPeople", string.Join(", ", people)];
        }

        if (filters.UndatedOnly)
        {
            about += text["Count_WithoutDate"];
        }

        if (filters.OnThisDay is { } onThisDay)
        {
            about += onThisDay.YearsAgo is { } ago ? text["Count_YearsAgo", ago] : text["Count_OnThisDay"];
        }

        return about;
    }

    private static int? Number(string? key) =>
        int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string MonthName(int month)
    {
        var name = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
        return name.Length == 0 ? name : CultureInfo.CurrentCulture.TextInfo.ToUpper(name[0]) + name[1..];
    }
}
