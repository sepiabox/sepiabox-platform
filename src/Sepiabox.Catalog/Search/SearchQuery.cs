using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>Source names, such as the drive's name given to <c>process</c>.</summary>
    public IReadOnlyList<string>? Sources { get; init; }

    /// <summary>Only assets with no capture date; cannot be combined with a time filter.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UndatedOnly { get; init; }

    /// <summary>True when no filter is set: the whole library.</summary>
    [JsonIgnore]
    public bool IsEmpty => this == new SearchFilters();

    [JsonIgnore]
    internal bool HasTimeFilter => DateFrom is not null || DateTo is not null || Years is not null || Months is not null || OnThisDay is not null;

    // Records compare lists by reference; equality here is by content, so a parsed query equals the one written.
    public bool Equals(SearchFilters? other) =>
        other is not null
        && Same(Kinds, other.Kinds) && DateFrom == other.DateFrom && DateTo == other.DateTo
        && Same(Years, other.Years) && Same(Months, other.Months) && OnThisDay == other.OnThisDay
        && Equals(Places, other.Places) && Same(Sources, other.Sources) && UndatedOnly == other.UndatedOnly;

    public override int GetHashCode() => HashCode.Combine(DateFrom, DateTo, OnThisDay, Places, UndatedOnly);

    internal static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b);
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
            ?? List("filters.sources", filters.Sources, Name)
            ?? (filters.UndatedOnly && filters.HasTimeFilter
                ? "filters.undatedOnly cannot be combined with dateFrom, dateTo, years, months or onThisDay."
                : null);
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
