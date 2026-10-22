using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sepiabox.Catalog.Events;

namespace Sepiabox.Catalog.Search;

/// <summary>
/// The search DSL v1 (D-020, S-013): what the filter bar, the API and later the question translator send to
/// <see cref="SearchService"/>. Schema: <c>docs/schemas/search.v1.json</c>. Parse it with
/// <see cref="SearchQueryJson.TryParse"/>, which rejects unknown fields and values out of range.
/// </summary>
public sealed record SearchQuery
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;

    public SearchFilters Filters { get; init; } = new();

    /// <summary><see cref="SearchSort.CapturedAtDesc"/> (newest first) or <see cref="SearchSort.CapturedAtAsc"/>; undated assets always last.</summary>
    public string Sort { get; init; } = SearchSort.CapturedAtDesc;

    public int Limit { get; init; } = DefaultLimit;

    /// <summary>
    /// Count instead of listing (S-026, D-075): the matching assets' total, or groups of them. <see cref="Sort"/> and
    /// <see cref="Limit"/> do not apply to a count.
    /// </summary>
    public SearchAggregate? Aggregate { get; init; }

    /// <summary>
    /// How many assets a search returns at most: <see cref="Limit"/>, or fewer when <see cref="SearchFilters.SimilarTo"/>
    /// asks for fewer (S-017).
    /// </summary>
    [JsonIgnore]
    public int ResultLimit => Filters.SimilarTo is { } similar ? Math.Min(Limit, similar.EffectiveLimit)
        : Filters.SimilarToRegion is { } region ? Math.Min(Limit, region.EffectiveLimit)
        : Limit;
}

/// <summary>What to count and how to group it (S-026). Later stories add <c>count: "events"</c> and other groupings.</summary>
public sealed record SearchAggregate
{
    public const int DefaultTop = 10;
    public const int MaxTop = 100;

    /// <summary>
    /// <see cref="SearchCount.Assets"/>: photos and videos, one per content (D-014); or <see cref="SearchCount.Events"/>:
    /// occasions with at least one matching asset (S-024). Required.
    /// </summary>
    [JsonRequired]
    public string Count { get; init; } = SearchCount.Assets;

    /// <summary>Absent or null for the total only; otherwise one of <see cref="SearchGroupBy"/>.</summary>
    public string? GroupBy { get; init; }

    /// <summary>
    /// The largest this many groups. Absent: every group by year, month or kind, in their order (at most
    /// <see cref="MaxTop"/>), or the largest <see cref="DefaultTop"/> by city or source. Only with <see cref="GroupBy"/>.
    /// </summary>
    public int? Top { get; init; }

    /// <summary>
    /// Also add up the matching assets' sizes (S-028, D-093): <see cref="SearchSum.Bytes"/>, or absent for the count only.
    /// Not with <see cref="SearchGroupBy.Person"/>: a person's photos are counted through their faces, not their sizes.
    /// </summary>
    public string? Sum { get; init; }

    public int EffectiveTop => Top ?? DefaultTop;
}

public static class SearchCount
{
    public const string Assets = "assets";

    /// <summary>The occasions that have a matching asset (S-024, D-094): an occasion counts once however many match.</summary>
    public const string Events = "events";
}

/// <summary>What a count can add up besides the number of assets (S-028).</summary>
public static class SearchSum
{
    /// <summary>The size of the originals, in bytes (<c>assets.size_bytes</c>).</summary>
    public const string Bytes = "bytes";
}

/// <summary>
/// The groupings of a count: the capture year and month (month of the year, 1-12), the kind, the city, the source, and
/// the named person in the photo (S-021).
/// </summary>
public static class SearchGroupBy
{
    public const string Year = "year";
    public const string Month = "month";
    public const string Kind = "kind";
    public const string City = "city";
    public const string Source = "source";
    public const string Person = "person";

    /// <summary>The occasion's type, only with <see cref="SearchCount.Events"/> (S-024); occasions without one are a group of their own.</summary>
    public const string EventType = "eventType";

    public static readonly IReadOnlyList<string> All = [Year, Month, Kind, City, Source, Person, EventType];
}

public static class SearchSort
{
    public const string CapturedAtDesc = "capturedAtDesc";
    public const string CapturedAtAsc = "capturedAtAsc";
}

/// <summary>Every filter given must match (AND); a list matches when any of its values does (OR).</summary>
public sealed record SearchFilters
{
    /// <summary><c>photo</c>, <c>video</c>.</summary>
    public IReadOnlyList<string>? Kinds { get; init; }

    /// <summary>First day included, on the camera's wall clock (D-037).</summary>
    public DateOnly? DateFrom { get; init; }

    /// <summary>Last day included.</summary>
    public DateOnly? DateTo { get; init; }

    public IReadOnlyList<int>? Years { get; init; }

    /// <summary>1–12, in any year unless <see cref="Years"/> says otherwise.</summary>
    public IReadOnlyList<int>? Months { get; init; }

    public OnThisDayFilter? OnThisDay { get; init; }

    public PlaceFilter? Places { get; init; }

    /// <summary>
    /// The family's places (S-014, D-083), by name, matched ignoring case: assets taken within any of them (inside its
    /// radius). An unknown name is an error.
    /// </summary>
    public IReadOnlyList<string>? SavedPlaces { get; init; }

    /// <summary>How far from one of the family's places, or from home, the asset was taken (S-014).</summary>
    public DistanceFilter? DistanceFrom { get; init; }

    /// <summary>Source names, such as the drive's name given to <c>process</c>.</summary>
    public IReadOnlyList<string>? Sources { get; init; }

    /// <summary>How many different sources the asset was found in (S-028, D-093): the same name twice counts once.</summary>
    public SourceCountFilter? SourceCount { get; init; }

    /// <summary>People who are all in the photo (S-021): named ones, and the signed-in user's own person.</summary>
    public PeopleFilter? People { get; init; }

    /// <summary>
    /// The occasions (S-023, D-092) whose assets match, by id: an asset in any of them. An id no occasion has matches
    /// nothing. Undated assets are in no occasion, so they never match. Only the occasions pages and links produce it;
    /// questions never do.
    /// </summary>
    public IReadOnlyList<long>? EventIds { get; init; }

    /// <summary>
    /// The types of occasion (S-024, D-094) the asset is in, any of them: <c>wedding</c>, <c>birthday</c>, <c>beach</c>,
    /// <c>christmas</c>, <c>graduation</c>, <c>snow</c>, <c>party</c> or <c>trip</c> (<see cref="OccasionTypes"/>).
    /// Assets in no occasion, or in one without a type, never match.
    /// </summary>
    public IReadOnlyList<string>? EventTypes { get; init; }

    /// <summary>
    /// A description in English matched against the photos' CLIP embeddings (S-016); results are then ordered by
    /// similarity, most similar first, and <see cref="SearchQuery.Sort"/> is not used.
    /// </summary>
    public VisualFilter? Visual { get; init; }

    /// <summary>
    /// The assets most similar to one asset by their stored CLIP embeddings (S-017, D-086), the asset itself left out;
    /// results are ordered by similarity, most similar first, and <see cref="SearchQuery.Sort"/> is not used. Not
    /// combined with <see cref="Visual"/> or <see cref="SearchQuery.Aggregate"/>; questions never produce it.
    /// </summary>
    public SimilarToFilter? SimilarTo { get; init; }

    /// <summary>
    /// The assets most similar to a region of one photo (S-041, D-099): the region is cut from the photo's preview and
    /// embedded with CLIP when asked. Ordered by similarity like <see cref="SimilarTo"/>, with the photo itself kept in
    /// (it holds the region), and not combined with <see cref="Visual"/>, <see cref="SimilarTo"/> or
    /// <see cref="SearchQuery.Aggregate"/>; questions never produce it.
    /// </summary>
    public SimilarToRegionFilter? SimilarToRegion { get; init; }

    /// <summary>Only assets with no capture date; cannot be combined with a time filter.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UndatedOnly { get; init; }

    /// <summary>True when no filter is set: the whole library.</summary>
    [JsonIgnore]
    public bool IsEmpty => this == new SearchFilters();

    /// <summary>True when a date, year, month or "on this day" filter is set: undated assets cannot match it.</summary>
    [JsonIgnore]
    public bool HasTimeFilter => DateFrom is not null || DateTo is not null || Years is not null || Months is not null || OnThisDay is not null;

    /// <summary>The same filters without the time ones, and only undated assets: what a time filter leaves out.</summary>
    [JsonIgnore]
    public SearchFilters UndatedInstead => this with { DateFrom = null, DateTo = null, Years = null, Months = null, OnThisDay = null, UndatedOnly = true };

    // Records compare lists by reference; equality here is by content, so a parsed query equals the one written.
    public bool Equals(SearchFilters? other) =>
        other is not null
        && Same(Kinds, other.Kinds) && DateFrom == other.DateFrom && DateTo == other.DateTo
        && Same(Years, other.Years) && Same(Months, other.Months) && OnThisDay == other.OnThisDay
        && Equals(Places, other.Places) && Same(SavedPlaces, other.SavedPlaces) && DistanceFrom == other.DistanceFrom
        && Same(Sources, other.Sources) && SourceCount == other.SourceCount && Equals(People, other.People) && Same(EventIds, other.EventIds) && Same(EventTypes, other.EventTypes) && UndatedOnly == other.UndatedOnly
        && Visual == other.Visual && SimilarTo == other.SimilarTo && SimilarToRegion == other.SimilarToRegion;

    public override int GetHashCode() => HashCode.Combine(DateFrom, DateTo, OnThisDay, Places, UndatedOnly);

    internal static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b);
}

/// <summary>
/// The number of different sources an asset has (S-028, D-093): exactly <see cref="Eq"/> (<c>{"eq": 1}</c> is "in only one
/// source"), or at least <see cref="Gte"/> (<c>{"gte": 2}</c> is "in several"); exactly one of the two.
/// </summary>
public sealed record SourceCountFilter
{
    public const int Max = 1000;

    public int? Eq { get; init; }

    public int? Gte { get; init; }
}

/// <summary>
/// People in the photo (S-021, D-080): every one of <see cref="All"/> (names as the family wrote them, matched ignoring
/// case) and, with <see cref="IncludeMe"/>, the person the signed-in user said "This is me" for (D-018). Only faces
/// counted for a person put them in a photo; excluded ones and those marked not a face never do (D-077, D-079).
/// </summary>
public sealed record PeopleFilter
{
    public IReadOnlyList<string>? All { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IncludeMe { get; init; }

    public bool Equals(PeopleFilter? other) => other is not null && SearchFilters.Same(All, other.All) && IncludeMe == other.IncludeMe;

    public override int GetHashCode() => HashCode.Combine(All?.Count, IncludeMe);
}

/// <summary>
/// The distance from a saved place's centre (S-014, D-083): from <see cref="Place"/> (a saved place's name) or from the
/// home (<see cref="Home"/>), exactly one of them; more than <see cref="MinKm"/> and at most <see cref="MaxKm"/>, at
/// least one of them. Assets without a location never match.
/// </summary>
public sealed record DistanceFilter
{
    public string? Place { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Home { get; init; }

    public double? MinKm { get; init; }

    public double? MaxKm { get; init; }
}

/// <summary>Search by description (S-016, D-070).</summary>
public sealed record VisualFilter
{
    /// <summary>The cosine similarity an asset needs when <see cref="MinScore"/> is absent.</summary>
    public const double DefaultMinScore = 0.2;

    /// <summary>What the photo shows, in English (D-017): "snow", "beach at sunset".</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The least cosine similarity, from 0 to 1, between the text and an asset's embedding.</summary>
    public double? MinScore { get; init; }

    public double EffectiveMinScore => MinScore ?? DefaultMinScore;
}

/// <summary>"More photos similar to this one" (S-017, D-086): the photo's own embedding, nothing recomputed.</summary>
public sealed record SimilarToFilter
{
    /// <summary>How many similar assets come back when <see cref="Limit"/> is absent.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The asset's id: the SHA-256 of its content, in lower-case hex (D-014).</summary>
    [JsonRequired]
    public string AssetId { get; init; } = string.Empty;

    /// <summary>The most similar assets returned, 1 to <see cref="SearchQuery.MaxLimit"/>; and never more than the query's limit.</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

/// <summary>"Photos like this part of a photo" (S-041, D-099): a box on the photo, relative to it.</summary>
public sealed record SimilarToRegionFilter
{
    /// <summary>The photo's id: the SHA-256 of its content, in lower-case hex (D-014).</summary>
    [JsonRequired]
    public string AssetId { get; init; } = string.Empty;

    /// <summary><c>[x, y, width, height]</c> as fractions of the photo's width and height, from its top left corner.</summary>
    [JsonRequired]
    public IReadOnlyList<double> Box { get; init; } = [];

    /// <summary>The most similar assets returned, as for <see cref="SimilarToFilter.Limit"/>.</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? SimilarToFilter.DefaultLimit;

    /// <summary>Whether <paramref name="box"/> is four numbers from 0 to 1, with a width and height above 0, inside the photo.</summary>
    public static bool IsValidBox(IReadOnlyList<double>? box) =>
        box is { Count: 4 }
        && box.All(double.IsFinite)
        && box[0] is >= 0 and <= 1 && box[1] is >= 0 and <= 1
        && box[2] > 0 && box[3] > 0
        && box[0] + box[2] <= 1 + BoxSlack && box[1] + box[3] <= 1 + BoxSlack;

    /// <summary>A box drawn to the very edge has x + width of 1 plus a rounding error.</summary>
    public const double BoxSlack = 1e-9;

    // The box is a list, which a record compares by reference.
    public bool Equals(SimilarToRegionFilter? other) =>
        other is not null && AssetId == other.AssetId && Limit == other.Limit && Box.SequenceEqual(other.Box);

    public override int GetHashCode() => HashCode.Combine(AssetId, Limit);
}

/// <summary>
/// The same day and month as today: <see cref="YearsAgo"/> years ago, or in every earlier year when it is absent
/// ("On this day"). Today is the date in <see cref="SearchOptions.TimeZone"/> (D-069).
/// </summary>
public sealed record OnThisDayFilter
{
    public int? YearsAgo { get; init; }
}

/// <summary>GeoNames place names as the catalog stores them (D-066), and ISO 3166 country codes.</summary>
public sealed record PlaceFilter
{
    /// <summary>City names, matched ignoring case and accents (D-069).</summary>
    public IReadOnlyList<string>? Cities { get; init; }

    /// <summary>Two-letter country codes, such as <c>PT</c>.</summary>
    public IReadOnlyList<string>? Countries { get; init; }

    public bool Equals(PlaceFilter? other) =>
        other is not null && SearchFilters.Same(Cities, other.Cities) && SearchFilters.Same(Countries, other.Countries);

    public override int GetHashCode() => HashCode.Combine(Cities?.Count, Countries?.Count);
}

/// <summary>Reading and writing the DSL as JSON, and as the <c>?q=</c> of a URL (base64url of the JSON).</summary>
public static class SearchQueryJson
{
    public const int MaxListLength = 50;
    public const int MaxNameLength = 200;
    public const int MinYear = 1800;
    public const int MaxYear = 9999;
    public const int MaxYearsAgo = 200;

    /// <summary>Half the Earth's circumference: no two points are farther apart.</summary>
    public const double MaxKm = 20_040;

    /// <summary>The longest description accepted; CLIP reads at most 77 tokens of it anyway.</summary>
    public const int MaxTextLength = 300;

    /// <summary>The longest <c>?q=</c> accepted; far more than any filter bar or question produces.</summary>
    public const int MaxEncodedLength = 8192;

    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static string Serialize(SearchQuery query) => JsonSerializer.Serialize(query, s_options);

    /// <summary>Parses and validates; on failure <paramref name="error"/> says what is wrong, in English.</summary>
    public static bool TryParse(string json, out SearchQuery query, out string error)
    {
        query = new SearchQuery();
        SearchQuery? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SearchQuery>(json, s_options);
        }
        catch (JsonException ex)
        {
            // A fixed message and the JSON path: the serializer's own text names internal .NET types.
            error = $"The search is not valid search DSL v1 JSON at {ex.Path ?? "$"}: an unknown or repeated field, a null, "
                + "a value of the wrong type, or malformed JSON.";
            return false;
        }

        if (parsed is null)
        {
            error = "The search is empty.";
            return false;
        }

        if (Validate(parsed) is { } problem)
        {
            error = problem;
            return false;
        }

        query = parsed;
        error = string.Empty;
        return true;
    }

    /// <summary>The query as the <c>?q=</c> of a URL: base64url (no padding) of its JSON.</summary>
    public static string Encode(SearchQuery query) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Serialize(query)));

    public static bool TryDecode(string? encoded, out SearchQuery query, out string error)
    {
        query = new SearchQuery();
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaxEncodedLength || !Base64Url.IsValid(encoded))
        {
            error = "The search in the address is not valid base64url.";
            return false;
        }

        string json;
        try
        {
            json = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Base64Url.DecodeFromChars(encoded));
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            error = "The search in the address is not valid base64url.";
            return false;
        }

        return TryParse(json, out query, out error);
    }

    /// <summary>The first rule the query breaks, or null.</summary>
    public static string? Validate(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Sort is not (SearchSort.CapturedAtDesc or SearchSort.CapturedAtAsc))
        {
            return $"sort must be '{SearchSort.CapturedAtDesc}' or '{SearchSort.CapturedAtAsc}'.";
        }

        if (query.Limit is < 1 or > SearchQuery.MaxLimit)
        {
            return string.Create(CultureInfo.InvariantCulture, $"limit must be between 1 and {SearchQuery.MaxLimit}.");
        }

        if (query.Aggregate is { } aggregate)
        {
            if (aggregate.Count is not (SearchCount.Assets or SearchCount.Events))
            {
                return $"aggregate.count must be '{SearchCount.Assets}' or '{SearchCount.Events}'.";
            }

            // Occasions are told apart by type; the other groupings are of assets.
            if (aggregate.Count == SearchCount.Events && aggregate.GroupBy is { } eventGroup && eventGroup != SearchGroupBy.EventType)
            {
                return $"aggregate.groupBy of a count of '{SearchCount.Events}' must be '{SearchGroupBy.EventType}'.";
            }

            if (aggregate.Count == SearchCount.Assets && aggregate.GroupBy == SearchGroupBy.EventType)
            {
                return $"aggregate.groupBy '{SearchGroupBy.EventType}' needs aggregate.count '{SearchCount.Events}'.";
            }

            if (aggregate.GroupBy is { } groupBy && !SearchGroupBy.All.Contains(groupBy))
            {
                return $"aggregate.groupBy must be one of {string.Join(", ", SearchGroupBy.All.Select(g => $"'{g}'"))}.";
            }

            if (aggregate.Sum is { } sum && sum != SearchSum.Bytes)
            {
                return $"aggregate.sum must be '{SearchSum.Bytes}'.";
            }

            if (aggregate is { Sum: not null, Count: SearchCount.Events })
            {
                return $"aggregate.sum cannot be added up for a count of '{SearchCount.Events}': an occasion has no size of its own.";
            }

            if (aggregate is { Sum: not null, GroupBy: SearchGroupBy.Person })
            {
                return "aggregate.sum cannot be added up per person: a photo with several people has one size.";
            }

            if (aggregate.Top is not null && aggregate.GroupBy is null)
            {
                return "aggregate.top needs aggregate.groupBy: a total has no groups to keep.";
            }

            if (aggregate.Top is < 1 or > SearchAggregate.MaxTop)
            {
                return string.Create(CultureInfo.InvariantCulture, $"aggregate.top must be between 1 and {SearchAggregate.MaxTop}.");
            }
        }

        var filters = query.Filters ?? throw new InvalidOperationException("filters is required.");
        return List("filters.kinds", filters.Kinds, kind => kind is "photo" or "video" ? null : "must be 'photo' or 'video'")
            ?? List("filters.years", filters.Years, year => year is >= MinYear and <= MaxYear ? null : $"must be between {MinYear} and {MaxYear}")
            ?? List("filters.months", filters.Months, month => month is >= 1 and <= 12 ? null : "must be between 1 and 12")
            ?? (filters.DateFrom > filters.DateTo ? "filters.dateFrom must not be after filters.dateTo." : null)
            ?? (filters.OnThisDay?.YearsAgo is < 0 or > MaxYearsAgo
                ? string.Create(CultureInfo.InvariantCulture, $"filters.onThisDay.yearsAgo must be between 0 and {MaxYearsAgo}.")
                : null)
            ?? List("filters.places.cities", filters.Places?.Cities, Name)
            ?? List("filters.places.countries", filters.Places?.Countries, code => code.Length == 2 && code.All(char.IsAsciiLetterUpper) ? null : "must be a two-letter country code such as 'PT'")
            ?? List("filters.savedPlaces", filters.SavedPlaces, Name)
            ?? Distance(filters.DistanceFrom)
            ?? List("filters.sources", filters.Sources, Name)
            ?? SourceCount(filters.SourceCount)
            ?? List("filters.eventIds", filters.EventIds, id => id >= 1 ? null : "must be an occasion id, 1 or more")
            ?? List("filters.eventTypes", filters.EventTypes, type => OccasionTypes.IsKnown(type) ? null : $"must be one of {string.Join(", ", OccasionTypes.All)}")
            ?? List("filters.people.all", filters.People?.All, Name)
            ?? (filters.People is { All: null, IncludeMe: false } ? "filters.people needs all, includeMe or both." : null)
            ?? Visual(filters.Visual)
            ?? SimilarTo(query)
            ?? SimilarToRegion(query)
            ?? (filters.UndatedOnly && filters.HasTimeFilter
                ? "filters.undatedOnly cannot be combined with dateFrom, dateTo, years, months or onThisDay."
                : null);
    }

    private static string? Distance(DistanceFilter? distance)
    {
        if (distance is null)
        {
            return null;
        }

        if ((distance.Place is null) == !distance.Home)
        {
            return "filters.distanceFrom needs exactly one of place or home.";
        }

        if (distance.Place is { } place && Name(place) is { } problem)
        {
            return $"filters.distanceFrom.place: '{place}' {problem}.";
        }

        if (distance is { MinKm: null, MaxKm: null })
        {
            return "filters.distanceFrom needs minKm, maxKm or both.";
        }

        if (distance.MinKm is { } min && !(min >= 0 && min <= MaxKm) || distance.MaxKm is { } max && !(max > 0 && max <= MaxKm))
        {
            return string.Create(CultureInfo.InvariantCulture, $"filters.distanceFrom.minKm and maxKm must be between 0 and {MaxKm} (maxKm above 0).");
        }

        return distance.MinKm >= distance.MaxKm ? "filters.distanceFrom.minKm must be less than maxKm." : null;
    }

    private static string? SourceCount(SourceCountFilter? count)
    {
        if (count is null)
        {
            return null;
        }

        if ((count.Eq is null) == (count.Gte is null))
        {
            return "filters.sourceCount needs exactly one of eq or gte.";
        }

        return count.Eq is < 1 or > SourceCountFilter.Max || count.Gte is < 1 or > SourceCountFilter.Max
            ? string.Create(CultureInfo.InvariantCulture, $"filters.sourceCount.eq and gte must be between 1 and {SourceCountFilter.Max}.")
            : null;
    }

    private static string? Visual(VisualFilter? visual)
    {
        if (visual is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(visual.Text) || visual.Text.Length > MaxTextLength || visual.Text.Any(char.IsControl))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"filters.visual.text must be a description of 1 to {MaxTextLength} characters without control characters.");
        }

        return visual.MinScore is { } score && !(score >= 0 && score <= 1)
            ? "filters.visual.minScore must be between 0 and 1."
            : null;
    }

    private static string? SimilarTo(SearchQuery query)
    {
        if (query.Filters.SimilarTo is not { } similar)
        {
            return null;
        }

        if (similar.AssetId.Length != 64 || !similar.AssetId.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
        {
            return "filters.similarTo.assetId must be an asset id: 64 lower-case hexadecimal characters.";
        }

        if (similar.Limit is < 1 or > SearchQuery.MaxLimit)
        {
            return string.Create(CultureInfo.InvariantCulture, $"filters.similarTo.limit must be between 1 and {SearchQuery.MaxLimit}.");
        }

        // One order by similarity at a time; and the most similar few are a list, not a number to count.
        return query.Filters.Visual is not null ? "filters.similarTo cannot be combined with filters.visual."
            : query.Filters.SimilarToRegion is not null ? "filters.similarTo cannot be combined with filters.similarToRegion."
            : query.Aggregate is not null ? "filters.similarTo cannot be combined with aggregate: it lists the most similar assets."
            : null;
    }

    private static string? SimilarToRegion(SearchQuery query)
    {
        if (query.Filters.SimilarToRegion is not { } region)
        {
            return null;
        }

        if (region.AssetId.Length != 64 || !region.AssetId.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
        {
            return "filters.similarToRegion.assetId must be an asset id: 64 lower-case hexadecimal characters.";
        }

        if (!SimilarToRegionFilter.IsValidBox(region.Box))
        {
            return "filters.similarToRegion.box must be [x, y, width, height]: numbers from 0 to 1, with a width and a height above 0, inside the photo.";
        }

        if (region.Limit is < 1 or > SearchQuery.MaxLimit)
        {
            return string.Create(CultureInfo.InvariantCulture, $"filters.similarToRegion.limit must be between 1 and {SearchQuery.MaxLimit}.");
        }

        return query.Filters.Visual is not null ? "filters.similarToRegion cannot be combined with filters.visual."
            : query.Aggregate is not null ? "filters.similarToRegion cannot be combined with aggregate: it lists the most similar assets."
            : null;
    }

    // Control characters, NUL among them, are never part of a name, and PostgreSQL refuses NUL in text.
    private static string? Name(string name) =>
        string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || name.Any(char.IsControl)
            ? $"must be a name of 1 to {MaxNameLength} characters without control characters"
            : null;

    private static string? List<T>(string field, IReadOnlyList<T>? values, Func<T, string?> check)
    {
        if (values is null)
        {
            return null;
        }

        if (values.Count is 0 or > MaxListLength)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{field} must have 1 to {MaxListLength} values.");
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                return $"{field} must not contain null.";
            }

            if (check(value) is { } problem)
            {
                return $"{field}: '{value}' {problem}.";
            }
        }

        return null;
    }
}
