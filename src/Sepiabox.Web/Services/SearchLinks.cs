using System.Globalization;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

/// <summary>What the filter bar shows: one value per field, or null for any.</summary>
public sealed record FilterFields(int? Year, int? Month, string? Kind, string? City, string? Source, string? Text = null)
{
    /// <summary>The people chosen (S-021): everyone the query names, since a photo must have them all.</summary>
    public IReadOnlyList<string> People { get; init; } = [];

    /// <summary>"With me": the signed-in user's own person.</summary>
    public bool Me { get; init; }
}

/// <summary>
/// The filter bar and the URL (S-013): the bar's fields become a search DSL query, and every filtered view lives
/// at <c>./?q=</c> plus the query in base64url, so a copied link opens the same filters (D-069).
/// </summary>
public static class SearchLinks
{
    public const string QueryParameter = "q";

    /// <summary>The "On this day" shortcut: today's day and month in every earlier year.</summary>
    public static SearchQuery OnThisDay { get; } = new() { Filters = new SearchFilters { OnThisDay = new OnThisDayFilter() } };

    /// <summary>The timeline with these filters (or this count, S-026); the plain timeline when there are none.</summary>
    public static string Timeline(SearchQuery? query) =>
        query is null || (query.Filters.IsEmpty && query.Aggregate is null) ? "./" : "./?" + QueryParameter + "=" + SearchQueryJson.Encode(query);

    /// <summary>
    /// The query the bar's submitted fields describe; values that are not a year, a month or a kind are ignored, as
    /// a hand-edited URL may hold anything.
    /// </summary>
    public static SearchQuery FromFields(
        string? year, string? month, string? kind, string? city, string? source, string? text = null, IEnumerable<string?>? people = null, bool me = false)
    {
        var names = (people ?? []).Select(Name).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(SearchQueryJson.MaxListLength).ToList();
        var filters = new SearchFilters
        {
            Years = Number(year, SearchQueryJson.MinYear, SearchQueryJson.MaxYear) is { } y ? [y] : null,
            Months = Number(month, 1, 12) is { } m ? [m] : null,
            Kinds = kind is AssetKinds.Photo or AssetKinds.Video ? [kind] : null,
            Places = Name(city) is { } c ? new PlaceFilter { Cities = [c] } : null,
            Sources = Name(source) is { } s ? [s] : null,
            Visual = Description(text) is { } t ? new VisualFilter { Text = t } : null,
            People = names.Count > 0 || me ? new PeopleFilter { All = names.Count > 0 ? names : null, IncludeMe = me } : null,
        };
        return new SearchQuery { Filters = filters };
    }

    /// <summary>The bar's fields for a query: a field shows a value only when the query has exactly one there.</summary>
    public static FilterFields ToFields(SearchQuery? query)
    {
        var filters = query?.Filters;
        return new FilterFields(
            SingleNumber(filters?.Years),
            SingleNumber(filters?.Months),
            Single(filters?.Kinds),
            filters?.Places?.Countries is null ? Single(filters?.Places?.Cities) : null,
            Single(filters?.Sources),
            filters?.Visual?.Text)
        {
            People = filters?.People?.All ?? [],
            Me = filters?.People?.IncludeMe ?? false,
        };
    }

    private static string? Single(IReadOnlyList<string>? values) => values is [var only] ? only : null;

    private static int? SingleNumber(IReadOnlyList<int>? values) => values is [var only] ? only : null;

    private static int? Number(string? value, int min, int max) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max
            ? number
            : null;

    // A description longer than the DSL allows is cut rather than dropped: CLIP reads only its start anyway.
    private static string? Description(string? value)
    {
        var text = value is null ? null : new string([.. value.Where(c => !char.IsControl(c))]).Trim();
        return string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, SearchQueryJson.MaxTextLength)].Trim();
    }

    private static string? Name(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > SearchQueryJson.MaxNameLength ? null : value.Trim();
}
