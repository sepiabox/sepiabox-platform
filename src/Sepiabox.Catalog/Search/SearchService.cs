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

/// <summary>How a search by description ranks its candidates (D-070).</summary>
public enum SimilarityMode
{
    /// <summary>
    /// Top results fast: without other filters, through the HNSW index, which is approximate and can miss some matches
    /// above the threshold; with other filters, exactly.
    /// </summary>
    Ranking,

    /// <summary>Every candidate's exact distance: every match above the threshold. For counting (S-025, S-026).</summary>
    Exact,
}

/// <summary>One asset a search found; <see cref="Score"/> is its cosine similarity to the description, for a visual search.</summary>
public sealed record SearchHit(
    string Id, string Kind, DateTime? CapturedAt, double? DurationSeconds, string? PlaceCity, string? PlaceCountry, double? Score = null);

/// <summary>The assets found, up to the query's limit; <see cref="HasMore"/> says whether more matched.</summary>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, bool HasMore);

/// <summary>One group of a count: its key (a year, a month 1-12, a kind, a city or a source; null for none) and its size.</summary>
public sealed record CountGroup(string? Key, int Count);

/// <summary>
/// A count (S-026): the matching assets' total, the largest groups when grouped, and, for a query with a time filter,
/// how many assets match the other filters but have no date (they cannot match a time filter).
/// </summary>
public sealed record CountResult(int Total, IReadOnlyList<CountGroup> Groups, int? Undated);

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

    public async Task<SearchResult> ExecuteAsync(
        SearchQuery query, SimilarityMode similarity = SimilarityMode.Ranking, CancellationToken cancellationToken = default)
    {
        var hits = await RunAsync(
            query,
            assets => assets.Select(a => new SearchHit(a.Id, a.Kind, a.CapturedAt, a.DurationSeconds, a.PlaceCity, a.PlaceCountry, null)),
            scored => scored.Select(s => new SearchHit(
                s.Asset.Id, s.Asset.Kind, s.Asset.CapturedAt, s.Asset.DurationSeconds, s.Asset.PlaceCity, s.Asset.PlaceCountry, 1 - s.Distance)),
            similarity,
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
        SimilarityMode similarity = SimilarityMode.Ranking,
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
        // The index walk only for top results without other filters, on a pgvector that can walk it iteratively; with
        // filters, for counting, or on an older pgvector, every candidate is ranked exactly (D-070).
        var walkIndex = similarity == SimilarityMode.Ranking
            && (query.Filters with { Visual = null }).IsEmpty
            && await PgvectorSimilarity.CanWalkIndexAsync(db, cancellationToken);
        var ranked = PgvectorSimilarity.Rank(db, Filtered(query), vector, visual.EffectiveMinScore, exact: !walkIndex);
        if (!walkIndex)
        {
            return await bySimilarity(ranked).Take(query.Limit + 1).ToListAsync(cancellationToken);
        }

        // One transaction, so the index settings apply to this query only and leave the pooled connection as it was.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PgvectorSimilarity.PrepareIndexWalkAsync(db, cancellationToken);
        var rows = await bySimilarity(ranked).Take(query.Limit + 1).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>
    /// Counts what the query matches (S-026, D-075), exactly, with plain EF Core grouping (D-051). Null for a query with
    /// a description: its matches depend on a similarity threshold, so no number is given in the MVP (D-070).
    /// </summary>
    public async Task<CountResult?> CountAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Filters.Visual is not null)
        {
            return null;
        }

        var assets = Filtered(query);
        var total = await assets.CountAsync(cancellationToken);
        var aggregate = query.Aggregate ?? new SearchAggregate();
        var groups = aggregate.GroupBy switch
        {
            null => [],
            SearchGroupBy.Year => Numbered(await assets.GroupBy(a => a.CapturedAt == null ? (int?)null : a.CapturedAt.Value.Year)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken), g => (g.Key, g.Count)),
            SearchGroupBy.Month => Numbered(await assets.GroupBy(a => a.CapturedAt == null ? (int?)null : a.CapturedAt.Value.Month)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken), g => (g.Key, g.Count)),
            SearchGroupBy.Kind => [.. (await assets.GroupBy(a => a.Kind).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
                .Select(g => new CountGroup(g.Key, g.Count))],
            SearchGroupBy.City => [.. (await assets.GroupBy(a => a.PlaceCity).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
                .Select(g => new CountGroup(g.Key, g.Count))],
            // An asset found on two drives counts once in each; twice on one drive, once.
            SearchGroupBy.Source => [.. (await assets.SelectMany(a => a.Sources.Select(s => new { a.Id, s.Name })).Distinct()
                .GroupBy(x => x.Name).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
                .Select(g => new CountGroup(g.Key, g.Count))],
            _ => throw new InvalidOperationException($"Unknown groupBy '{aggregate.GroupBy}'."),
        };
        int? undated = query.Filters.HasTimeFilter
            ? await Filter(db.Assets.AsNoTracking(), query.Filters.UndatedInstead).CountAsync(cancellationToken)
            : null;
        return new CountResult(total, [.. Largest(groups).Take(aggregate.EffectiveTop)], undated);
    }

    private static List<CountGroup> Numbered<T>(IEnumerable<T> groups, Func<T, (int? Key, int Count)> select) =>
        [.. groups.Select(select).Select(g => new CountGroup(g.Key?.ToString(CultureInfo.InvariantCulture), g.Count))];

    // Largest first; between equal counts, years and months in order and names alphabetically, "none" last.
    private static IEnumerable<CountGroup> Largest(IEnumerable<CountGroup> groups) =>
        groups.OrderByDescending(g => g.Count)
            .ThenBy(g => g.Key is null)
            .ThenBy(g => int.TryParse(g.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
            .ThenBy(g => g.Key, StringComparer.Ordinal);

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
