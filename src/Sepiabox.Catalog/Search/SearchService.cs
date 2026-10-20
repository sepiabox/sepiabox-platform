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

/// <summary>One asset a search found; <see cref="Score"/> is its cosine similarity to the description, for a visual search.</summary>
public sealed record SearchHit(
    string Id, string Kind, DateTime? CapturedAt, double? DurationSeconds, string? PlaceCity, string? PlaceCountry, double? Score = null);

/// <summary>The assets found, up to the query's limit; <see cref="HasMore"/> says whether more matched.</summary>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, bool HasMore);

/// <summary>
/// Runs the search DSL (S-013) with plain EF Core (D-051): every value goes to the database as a parameter, never
/// into SQL text. Filters are combined with AND; the values of one list with OR.
/// </summary>
public sealed class SearchService(
    CatalogDbContext db, TimeProvider clock, SearchOptions? options = null, IQueryEmbedder? embedder = null)
{
    /// <summary>
    /// The assets the query's filters match, in no particular order and without its limit: for a caller that orders
    /// by something else, such as visual similarity (S-016).
    /// </summary>
    public IQueryable<Asset> Filtered(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Filter(db.Assets.AsNoTracking(), query.Filters);
    }

    /// <summary>
    /// The assets the query matches, sorted by capture date (undated last, then by id), without its limit. A query with
    /// <see cref="SearchFilters.Visual"/> needs its vector: use <see cref="RunAsync{T}"/>.
    /// </summary>
    public IQueryable<Asset> Query(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Filters.Visual is not null)
        {
            throw new InvalidOperationException("A search by description is run with RunAsync, which computes its vector.");
        }

        var assets = Filtered(query);
        // Undated last in both directions; the id makes the order total, so pages never overlap.
        return query.Sort == SearchSort.CapturedAtAsc
            ? assets.OrderBy(a => a.CapturedAt == null).ThenBy(a => a.CapturedAt).ThenBy(a => a.Id)
            : assets.OrderBy(a => a.CapturedAt == null).ThenByDescending(a => a.CapturedAt).ThenBy(a => a.Id);
    }

    public async Task<SearchResult> ExecuteAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var hits = await RunAsync(
            query,
            assets => assets.Select(a => new SearchHit(a.Id, a.Kind, a.CapturedAt, a.DurationSeconds, a.PlaceCity, a.PlaceCountry, null)),
            scored => scored.Select(s => new SearchHit(
                s.Asset.Id, s.Asset.Kind, s.Asset.CapturedAt, s.Asset.DurationSeconds, s.Asset.PlaceCity, s.Asset.PlaceCountry, 1 - s.Distance)),
            cancellationToken);
        return new SearchResult([.. hits.Take(query.Limit)], hits.Count > query.Limit);
    }

    /// <summary>
    /// Runs the query and returns up to its limit plus one row (so a caller can tell whether more matched), projected
    /// by <paramref name="byDate"/> for a plain search or <paramref name="bySimilarity"/> for a search by description,
    /// whose vector comes from the embedder (throws <see cref="VisualSearchUnavailableException"/> without one).
    /// </summary>
    public async Task<List<T>> RunAsync<T>(
        SearchQuery query,
        Func<IQueryable<Asset>, IQueryable<T>> byDate,
        Func<IQueryable<ScoredAsset>, IQueryable<T>> bySimilarity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(byDate);
        ArgumentNullException.ThrowIfNull(bySimilarity);
        if (query.Filters.Visual is not { } visual)
        {
            return await byDate(Query(query)).Take(query.Limit + 1).ToListAsync(cancellationToken);
        }

        if (embedder is not { IsAvailable: true })
        {
            throw new VisualSearchUnavailableException("Search by description is not available: the CLIP text model is not loaded.");
        }

        var vector = embedder.Embed(visual.Text);
        // With other filters, every candidate is ranked exactly: an index walk can miss what the filters keep (D-070).
        var filtered = !(query.Filters with { Visual = null }).IsEmpty;
        var ranked = PgvectorSimilarity.Rank(db, Filtered(query), vector, visual.EffectiveMinScore, exact: filtered);
        // One transaction, so the index setting applies to this query only and leaves the pooled connection as it was.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PgvectorSimilarity.PrepareAsync(db, cancellationToken);
        var rows = await bySimilarity(ranked).Take(query.Limit + 1).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
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

        // The last day there is has no day after it to stop before: nothing is later.
        if (filters.DateTo is { } to && to < DateOnly.MaxValue)
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
            // From 29 February, N years ago in a common year is the 28th (DateOnly.AddYears); from 28 February of a
            // common year, a leap year N years ago adds its 29th, as the shortcut without yearsAgo does.
            var day = today.AddYears(-yearsAgo);
            var days = IsLeapDayStandIn(today) && DateTime.IsLeapYear(day.Year) ? 2 : 1;
            var start = day.ToDateTime(TimeOnly.MinValue);
            var end = day.AddDays(days).ToDateTime(TimeOnly.MinValue);
            return assets.Where(a => a.CapturedAt >= start && a.CapturedAt < end);
        }

        var (month, dayOfMonth, year) = (today.Month, today.Day, today.Year);
        // On 28 February of a year without a 29th, the 29ths of leap years are shown too.
        var leapDay = IsLeapDayStandIn(today);
        return assets.Where(a => a.CapturedAt != null && a.CapturedAt.Value.Year < year
            && a.CapturedAt.Value.Month == month
            && (a.CapturedAt.Value.Day == dayOfMonth || (leapDay && a.CapturedAt.Value.Day == 29)));
    }

    private static bool IsLeapDayStandIn(DateOnly today) => today is { Month: 2, Day: 28 } && !DateTime.IsLeapYear(today.Year);

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
