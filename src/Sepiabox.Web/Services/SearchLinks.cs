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

    /// <summary>The family's places chosen (S-014): taken in any of them.</summary>
    public IReadOnlyList<string> SavedPlaces { get; init; } = [];

    /// <summary>The photo whose similar ones are shown (S-017), which the bar keeps while it narrows them.</summary>
    public SimilarToFilter? SimilarTo { get; init; }

    /// <summary>The region of a photo whose similar ones are shown (S-041), which the bar keeps while it narrows them.</summary>
    public SimilarToRegionFilter? SimilarToRegion { get; init; }

    /// <summary>The occasions chosen (S-023), which the bar keeps while it narrows them; it offers no way to choose one.</summary>
    public IReadOnlyList<long> EventIds { get; init; } = [];

    /// <summary>The number of sources chosen (S-028), which the bar keeps while it narrows them; it offers no way to choose one.</summary>
    public SourceCountFilter? SourceCount { get; init; }

    /// <summary>Blurry or sharp photos only (S-027), which the bar keeps while it narrows them; it offers no way to choose it.</summary>
    public QualityFilter? Quality { get; init; }

    /// <summary>Including what was set aside (S-027), kept the same way.</summary>
    public bool IncludeHidden { get; init; }
}

/// <summary>
/// The filter bar and the URL (S-013): the bar's fields become a search DSL query, and every filtered view lives
/// at <c>./?q=</c> plus the query in base64url, so a copied link opens the same filters (D-069).
/// </summary>
public static class SearchLinks
{
    public const string QueryParameter = "q";

    /// <summary>The bar's hidden fields for a search for similar ones (S-017): the asset's id, and the number asked for.</summary>
    public const string SimilarParameter = "similar";

    public const string SimilarLimitParameter = "similar-limit";

    /// <summary>
    /// The hidden fields of a search for a region of a photo (S-041, D-099), which the photo's page also submits: the
    /// asset's id, the box as <c>x,y,width,height</c> and the number asked for.
    /// </summary>
    public const string RegionAssetParameter = "region-asset";

    public const string RegionBoxParameter = "region";

    public const string RegionLimitParameter = "region-limit";

    /// <summary>The bar's hidden fields for a search inside occasions (S-023): one per occasion id.</summary>
    public const string EventParameter = "event";

    /// <summary>The bar's hidden fields for the sharpness filter (<c>1</c> blurry, <c>0</c> sharp) and for including what was set aside (S-027).</summary>
    public const string BlurryParameter = "blurry";

    public const string HiddenParameter = "hidden";

    /// <summary>The bar's hidden fields for a search by the number of sources (S-028): exactly, or at least.</summary>
    public const string SourceEqParameter = "source-eq";

    public const string SourceGteParameter = "source-gte";

    /// <summary>The "On this day" shortcut: today's day and month in every earlier year.</summary>
    public static SearchQuery OnThisDay { get; } = new() { Filters = new SearchFilters { OnThisDay = new OnThisDayFilter() } };

    /// <summary>"More photos similar to this one" (S-017): the photo's own embedding, the default number of results.</summary>
    public static SearchQuery Similar(string assetId) =>
        new() { Filters = new SearchFilters { SimilarTo = new SimilarToFilter { AssetId = assetId } } };

    /// <summary>A box as the address writes it: four numbers with up to 4 decimals, comma separated.</summary>
    public static string FormatBox(IReadOnlyList<double> box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return string.Join(',', box.Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)));
    }

    /// <summary>The box an address holds, or null when it is not four numbers that make a box inside the photo (S-041).</summary>
    public static IReadOnlyList<double>? ParseBox(string? value)
    {
        var parts = value?.Split(',');
        if (parts is not { Length: 4 })
        {
            return null;
        }

        var box = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out box[i]))
            {
                return null;
            }
        }

        return SimilarToRegionFilter.IsValidBox(box) ? box : null;
    }

    /// <summary>"Photos like this part of a photo" (S-041): the region, the default number of results.</summary>
    public static SearchQuery Region(string assetId, IReadOnlyList<double> box) =>
        new() { Filters = new SearchFilters { SimilarToRegion = new SimilarToRegionFilter { AssetId = assetId, Box = box } } };

    /// <summary>The timeline with these filters (or this count, S-026); the plain timeline when there are none.</summary>
    public static string Timeline(SearchQuery? query) =>
        query is null || (query.Filters.IsEmpty && query.Aggregate is null) ? "./" : "./?" + QueryParameter + "=" + SearchQueryJson.Encode(query);

    /// <summary>
    /// The query the bar's submitted fields describe; values that are not a year, a month or a kind are ignored, as
    /// a hand-edited URL may hold anything.
    /// </summary>
    public static SearchQuery FromFields(
        string? year,
        string? month,
        string? kind,
        string? city,
        string? source,
        string? text = null,
        IEnumerable<string?>? people = null,
        bool me = false,
        IEnumerable<string?>? savedPlaces = null,
        string? similar = null,
        string? similarLimit = null,
        IEnumerable<string?>? eventIds = null,
        string? sourceEq = null,
        string? sourceGte = null,
        string? regionAsset = null,
        string? regionBox = null,
        string? regionLimit = null,
        string? blurry = null,
        string? includeHidden = null)
    {
        var events = EventIds(eventIds);
        var names = Names(people);
        var places = Names(savedPlaces);
        var visual = Description(text) is { } t ? new VisualFilter { Text = t } : null;
        // A description typed in the bar is a new search by similarity, which replaces the one to a photo (D-086).
        var region = visual is null && regionAsset is not null && AssetIds.IsValid(regionAsset) && ParseBox(regionBox) is { } box
            ? new SimilarToRegionFilter { AssetId = regionAsset, Box = box, Limit = Number(regionLimit, 1, SearchQuery.MaxLimit) }
            : null;
        var similarTo = visual is null && region is null && similar is not null && AssetIds.IsValid(similar)
            ? new SimilarToFilter { AssetId = similar, Limit = Number(similarLimit, 1, SearchQuery.MaxLimit) }
            : null;
        var filters = new SearchFilters
        {
            Years = Number(year, SearchQueryJson.MinYear, SearchQueryJson.MaxYear) is { } y ? [y] : null,
            Months = Number(month, 1, 12) is { } m ? [m] : null,
            Kinds = kind is AssetKinds.Photo or AssetKinds.Video ? [kind] : null,
            Places = Name(city) is { } c ? new PlaceFilter { Cities = [c] } : null,
            Sources = Name(source) is { } s ? [s] : null,
            Visual = visual,
            SimilarTo = similarTo,
            SimilarToRegion = region,
            People = names.Count > 0 || me ? new PeopleFilter { All = names.Count > 0 ? names : null, IncludeMe = me } : null,
            SavedPlaces = places.Count > 0 ? places : null,
            EventIds = events.Count > 0 ? events : null,
            SourceCount = SourceCount(sourceEq, sourceGte),
            Quality = blurry is "1" or "0" ? new QualityFilter { Blurry = blurry == "1" } : null,
            IncludeHidden = includeHidden == "1",
        };
        // As many as were asked for: the query's own limit would otherwise cap a raised one (SearchQuery.ResultLimit).
        return (similarTo?.Limit ?? region?.Limit) is > SearchQuery.DefaultLimit and var raised
            ? new SearchQuery { Filters = filters, Limit = raised }
            : new SearchQuery { Filters = filters };
    }

    // One of the two, a whole number the DSL accepts; anything else a hand-edited address holds is ignored.
    private static SourceCountFilter? SourceCount(string? eq, string? gte) =>
        Number(eq, 1, SourceCountFilter.Max) is { } exactly ? new SourceCountFilter { Eq = exactly }
        : Number(gte, 1, SourceCountFilter.Max) is { } atLeast ? new SourceCountFilter { Gte = atLeast }
        : null;

    // Ids of occasions are positive whole numbers; anything else a hand-edited address holds is ignored.
    private static List<long> EventIds(IEnumerable<string?>? values) =>
        [.. (values ?? [])
            .Select(v => long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id >= 1 ? id : 0)
            .Where(id => id > 0).Distinct().Take(SearchQueryJson.MaxListLength)];

    private static List<string> Names(IEnumerable<string?>? values) =>
        [.. (values ?? []).Select(Name).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(SearchQueryJson.MaxListLength)];

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
            SavedPlaces = filters?.SavedPlaces ?? [],
            SimilarTo = filters?.SimilarTo,
            SimilarToRegion = filters?.SimilarToRegion,
            EventIds = filters?.EventIds ?? [],
            SourceCount = filters?.SourceCount,
            Quality = filters?.Quality,
            IncludeHidden = filters?.IncludeHidden ?? false,
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
