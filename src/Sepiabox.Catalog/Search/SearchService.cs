using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Sepiabox.Catalog.Search;

/// <summary>Settings for the search: the time zone in which "today" is taken (D-069).</summary>
public sealed record SearchOptions
{
    /// <summary>Null: the clock's local time zone (UTC in Container Apps).</summary>
    public TimeZoneInfo? TimeZone { get; init; }
}

/// <summary>One asset a search found.</summary>
public sealed record SearchHit(string Id, string Kind, DateTime? CapturedAt, double? DurationSeconds, string? PlaceCity, string? PlaceCountry);

/// <summary>The assets found, up to the query's limit; <see cref="HasMore"/> says whether more matched.</summary>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, bool HasMore);

/// <summary>
/// Runs the search DSL (S-013) with plain EF Core (D-051): every value goes to the database as a parameter, never
/// into SQL text. Filters are combined with AND; the values of one list with OR.
/// </summary>
public sealed class SearchService(CatalogDbContext db, TimeProvider clock, SearchOptions? options = null)
{
    /// <summary>The assets the query matches, sorted (undated last, then by id), without its limit.</summary>
    public IQueryable<Asset> Query(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var assets = Filter(db.Assets.AsNoTracking(), query.Filters);
        // Undated last in both directions; the id makes the order total, so pages never overlap.
        return query.Sort == SearchSort.CapturedAtAsc
            ? assets.OrderBy(a => a.CapturedAt == null).ThenBy(a => a.CapturedAt).ThenBy(a => a.Id)
            : assets.OrderBy(a => a.CapturedAt == null).ThenByDescending(a => a.CapturedAt).ThenBy(a => a.Id);
    }

    public async Task<SearchResult> ExecuteAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var hits = await Query(query)
            .Select(a => new SearchHit(a.Id, a.Kind, a.CapturedAt, a.DurationSeconds, a.PlaceCity, a.PlaceCountry))
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        return new SearchResult([.. hits.Take(query.Limit)], hits.Count > query.Limit);
    }

    /// <summary>Today's date where the family is, for "on this day".</summary>
    public DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), options?.TimeZone ?? clock.LocalTimeZone).DateTime);

    private IQueryable<Asset> Filter(IQueryable<Asset> assets, SearchFilters filters)
    {
        if (filters.Kinds is { } kinds)
        {
            assets = assets.Where(a => kinds.Contains(a.Kind));
        }

        if (filters.DateFrom is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            assets = assets.Where(a => a.CapturedAt >= start);
        }

        if (filters.DateTo is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            assets = assets.Where(a => a.CapturedAt < end);
        }

        if (filters.Years is { } years)
        {
            assets = assets.Where(a => a.CapturedAt != null && years.Contains(a.CapturedAt.Value.Year));
        }

        if (filters.Months is { } months)
        {
            assets = assets.Where(a => a.CapturedAt != null && months.Contains(a.CapturedAt.Value.Month));
        }

        if (filters.OnThisDay is { } onThisDay)
        {
            assets = OnThisDay(assets, onThisDay);
        }

        if (filters.UndatedOnly)
        {
            assets = assets.Where(a => a.CapturedAt == null);
        }

        if (filters.Places?.Cities is { } cities)
        {
            assets = InCities(assets, cities);
        }

        if (filters.Places?.Countries is { } countries)
        {
            // The catalog keeps the country's name (D-066); GeoNames maps codes to the same names.
            var names = db.GeoNames.Where(g => countries.Contains(g.CountryCode) && g.CountryName != null).Select(g => g.CountryName);
            assets = assets.Where(a => a.PlaceCountry != null && names.Contains(a.PlaceCountry));
        }

        if (filters.Sources is { } sources)
        {
            var lowered = sources.Select(s => s.ToLowerInvariant()).ToList();
            assets = assets.Where(a => a.Sources.Any(s => lowered.Contains(s.Name.ToLower())));
        }

        return assets;
    }

    private IQueryable<Asset> OnThisDay(IQueryable<Asset> assets, OnThisDayFilter filter)
    {
        var today = Today();
        if (filter.YearsAgo is { } yearsAgo)
        {
            // 29 February a year later is 28 February, as DateOnly.AddYears does.
            var day = today.AddYears(-yearsAgo);
            var start = day.ToDateTime(TimeOnly.MinValue);
            var end = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
            return assets.Where(a => a.CapturedAt >= start && a.CapturedAt < end);
        }

        var (month, dayOfMonth, year) = (today.Month, today.Day, today.Year);
        // On 28 February of a year without a 29th, the 29ths of leap years are shown too.
        var leapDay = month == 2 && dayOfMonth == 28 && !DateTime.IsLeapYear(year);
        return assets.Where(a => a.CapturedAt != null && a.CapturedAt.Value.Year < year
            && a.CapturedAt.Value.Month == month
            && (a.CapturedAt.Value.Day == dayOfMonth || (leapDay && a.CapturedAt.Value.Day == 29)));
    }

    private IQueryable<Asset> InCities(IQueryable<Asset> assets, IReadOnlyList<string> cities)
    {
        // Case and accents ignored (D-069): "lisbon" and "LISBON" find "Lisbon", "sao paulo" finds "São Paulo"
        // through GeoNames' ASCII name; the stored names that match are then compared ignoring case.
        var lowered = cities.Select(c => c.Trim().ToLowerInvariant()).Distinct().ToList();
        var folded = lowered.Select(Fold).Distinct().ToList();
        var known = db.GeoNames
            .Where(g => folded.Contains(g.AsciiName.ToLower()) || lowered.Contains(g.Name.ToLower()))
            .Select(g => g.Name.ToLower());
        return assets.Where(a => a.PlaceCity != null
            && (lowered.Contains(a.PlaceCity.ToLower()) || known.Contains(a.PlaceCity.ToLower())));
    }

    /// <summary>Lower case without diacritics: "São Paulo" → "sao paulo".</summary>
    public static string Fold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var kept = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return new string([.. kept]).Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
