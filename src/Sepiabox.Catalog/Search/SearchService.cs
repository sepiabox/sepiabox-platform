using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog.People;

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

/// <summary>
/// A search names people the catalog does not have (S-021, D-080): <see cref="Missing"/> lists the names, and
/// <see cref="MeMissing"/> says the signed-in user asked for themselves without having said "This is me".
/// </summary>
public sealed class UnknownPeopleException(IReadOnlyList<string> missing, bool meMissing)
    : Exception(Describe(missing, meMissing))
{
    public IReadOnlyList<string> Missing { get; } = missing;

    public bool MeMissing { get; } = meMissing;

    private static string Describe(IReadOnlyList<string> missing, bool meMissing) =>
        string.Join(" ", new[]
        {
            missing.Count > 0 ? $"No one is called {string.Join(", ", missing)}." : null,
            meMissing ? "The signed-in user has not said \"This is me\" for any person." : null,
        }.Where(part => part is not null));
}

/// <summary>
/// A search names saved places the catalog does not have (S-014): <see cref="Missing"/> lists them, as written;
/// <see cref="HomeMissing"/> says it asked for the distance from home and no place is the home.
/// </summary>
public sealed class UnknownPlacesException(IReadOnlyList<string> missing, bool homeMissing)
    : Exception(Describe(missing, homeMissing))
{
    public IReadOnlyList<string> Missing { get; } = missing;

    public bool HomeMissing { get; } = homeMissing;

    private static string Describe(IReadOnlyList<string> missing, bool homeMissing) =>
        string.Join(" ", new[]
        {
            missing.Count > 0 ? $"No saved place is called {string.Join(", ", missing)}." : null,
            homeMissing ? "No saved place is marked as home." : null,
        }.Where(part => part is not null));
}

/// <summary>
/// The saved places a search names (S-014, D-083), read before it runs by <see cref="SearchService.ResolvePlacesAsync"/>:
/// by the cleaned name the filters gave, and the home. Passed to the query explicitly, so nothing about one search is
/// kept on the service.
/// </summary>
public sealed record ResolvedPlaces(IReadOnlyDictionary<string, SavedPlace> ByName, SavedPlace? Home)
{
    public static readonly ResolvedPlaces None = new(new Dictionary<string, SavedPlace>(), null);

    /// <summary>The place a filter names; throws when it was not resolved for this search.</summary>
    public SavedPlace Named(string name) => ByName.TryGetValue(SearchService.PlaceKey(name), out var place)
        ? place
        : throw new InvalidOperationException($"Resolve the places with ResolvePlacesAsync before filtering by the saved place '{name}'.");
}

/// <summary>One asset a search found; <see cref="Score"/> is its cosine similarity to the description, for a visual search.</summary>
public sealed record SearchHit(
    string Id, string Kind, DateTime? CapturedAt, double? DurationSeconds, string? PlaceCity, string? PlaceCountry, double? Score = null);

/// <summary>The assets found, up to the query's limit; <see cref="HasMore"/> says whether more matched.</summary>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, bool HasMore);

/// <summary>One group of a count: its key (a year, a month 1-12, a kind, a city or a source; null for none) and its size.</summary>
/// <param name="Key">The year, month, kind, city, source or person; null for none.</param>
/// <param name="Count">How many assets.</param>
/// <param name="Bytes">Their total size when the query asked for <see cref="SearchSum.Bytes"/>; otherwise null.</param>
public sealed record CountGroup(string? Key, int Count, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? Bytes = null);

/// <summary>
/// A count (S-026): the matching assets' total, the groups shown when grouped, and, for a query with a time filter,
/// how many assets match the other filters but have no date (they cannot match a time filter). <see cref="GroupCount"/>
/// is how many groups there are in all, more than <see cref="Groups"/> when only the largest are shown.
/// </summary>
public sealed record CountResult(int Total, IReadOnlyList<CountGroup> Groups, int? Undated, int? GroupCount = null)
{
    /// <summary>Every group, shown or not.</summary>
    public int AllGroups => GroupCount ?? Groups.Count;

    /// <summary>The matching assets' total size in bytes, when the query asked for <see cref="SearchSum.Bytes"/> (S-028).</summary>
    public long? Bytes { get; init; }
}

/// <summary>
/// Runs the search DSL (S-013) with plain EF Core (D-051): every value goes to the database as a parameter, never
/// into SQL text. Filters are combined with AND; the values of one list with OR.
/// </summary>
/// <remarks>
/// <paramref name="userEmail"/> is the signed-in user's address, for <see cref="PeopleFilter.IncludeMe"/> (S-021); it is
/// only compared with the catalog's "This is me" links, never sent anywhere.
/// </remarks>
public sealed class SearchService(
    CatalogDbContext db,
    TimeProvider clock,
    SearchOptions? options = null,
    IQueryEmbedder? embedder = null,
    string? userEmail = null,
    IRegionEmbedder? regionEmbedder = null)
{
    private readonly string? _me = string.IsNullOrWhiteSpace(userEmail) ? null : userEmail.Trim().ToLowerInvariant();

    /// <summary>
    /// The people the filters name, by id: every name must be a person's (ignoring case) and, with includeMe, the user
    /// must have said "This is me"; otherwise <see cref="UnknownPeopleException"/>. Run before a search or a count.
    /// </summary>
    public async Task<IReadOnlyList<long>> ResolvePeopleAsync(SearchFilters filters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.People is not { } people)
        {
            return [];
        }

        var ids = new List<long>();
        var missing = new List<string>();
        foreach (var name in people.All ?? [])
        {
            string[] lowered = [NameKey(name)];
            var id = await db.People.AsNoTracking().Where(p => p.Name != null && lowered.Contains(p.Name.ToLower()))
                .Select(p => (long?)p.Id).FirstOrDefaultAsync(cancellationToken);
            if (id is { } found)
            {
                ids.Add(found);
            }
            else
            {
                missing.Add(name);
            }
        }

        var meMissing = false;
        if (people.IncludeMe)
        {
            var me = _me is null
                ? null
                : await db.People.AsNoTracking().Where(p => p.UserEmail == _me).Select(p => (long?)p.Id).FirstOrDefaultAsync(cancellationToken);
            if (me is { } myId)
            {
                ids.Add(myId);
            }
            else
            {
                meMissing = true;
            }
        }

        return missing.Count > 0 || meMissing ? throw new UnknownPeopleException(missing, meMissing) : [.. ids.Distinct()];
    }

    /// <summary>
    /// Reads the saved places the filters name (ignoring case, with PostgreSQL's <c>lower()</c> on both sides, as the
    /// unique index compares them) and the home, for the search to filter by; throws
    /// <see cref="UnknownPlacesException"/> when one is not there or the distance from home has no home. What it returns is
    /// what <see cref="Filtered"/> and <see cref="Query"/> filter by.
    /// </summary>
    public async Task<ResolvedPlaces> ResolvePlacesAsync(SearchFilters filters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.SavedPlaces is null && filters.DistanceFrom is null)
        {
            return ResolvedPlaces.None;
        }

        var written = (filters.SavedPlaces ?? []).Concat(filters.DistanceFrom?.Place is { } place ? [place] : []).Distinct().ToList();
        var cleaned = written.Select(PlaceKey).Distinct().ToArray();
        // One query for every name: which place, if any, each one is.
        var matches = cleaned.Length == 0
            ? []
            : await db.Database.SqlQuery<PlaceMatch>($"""
                SELECT n AS "Name", p.id AS "Id"
                FROM unnest({cleaned}) AS n
                LEFT JOIN saved_places p ON lower(p.name) = lower(n)
                """).ToListAsync(cancellationToken);
        var ids = matches.Where(m => m.Id is not null).Select(m => m.Id!.Value).ToList();
        var places = await db.SavedPlaces.AsNoTracking().Where(p => ids.Contains(p.Id) || p.IsHome).ToListAsync(cancellationToken);
        var byName = matches.Where(m => m.Id is not null)
            .ToDictionary(m => m.Name, m => places.Single(p => p.Id == m.Id), StringComparer.Ordinal);
        var home = places.FirstOrDefault(p => p.IsHome);
        var missing = written.Where(name => !byName.ContainsKey(PlaceKey(name))).ToList();
        var homeMissing = filters.DistanceFrom is { Home: true } && home is null;
        return missing.Count > 0 || homeMissing
            ? throw new UnknownPlacesException(missing, homeMissing)
            : new ResolvedPlaces(byName, home);
    }

    /// <summary>
    /// The assets the query's filters match, in no particular order and without its limit: for a caller that orders
    /// by something else, such as visual similarity (S-016).
    /// </summary>
    /// <param name="query">The search.</param>
    /// <param name="places">The saved places its filters name, from <see cref="ResolvePlacesAsync"/>; none when they name none.</param>
    public IQueryable<Asset> Filtered(SearchQuery query, ResolvedPlaces? places = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Filter(db.Assets.AsNoTracking(), query.Filters, places ?? ResolvedPlaces.None);
    }

    /// <summary>
    /// The assets the query matches, sorted by capture date (undated last, then by id), without its limit. A query with
    /// <see cref="SearchFilters.Visual"/> or <see cref="SearchFilters.SimilarTo"/> needs its vector: use <see cref="RunAsync{T}"/>.
    /// </summary>
    /// <param name="query">The search.</param>
    /// <param name="places">The saved places its filters name, from <see cref="ResolvePlacesAsync"/>; none when they name none.</param>
    public IQueryable<Asset> Query(SearchQuery query, ResolvedPlaces? places = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Filters.Visual is not null || query.Filters.SimilarTo is not null || query.Filters.SimilarToRegion is not null)
        {
            throw new InvalidOperationException("A search by similarity is run with RunAsync, which finds its vector.");
        }

        var assets = Filtered(query, places);
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
        return new SearchResult([.. hits.Take(query.ResultLimit)], hits.Count > query.ResultLimit);
    }

    /// <summary>
    /// Runs the query and returns up to its <see cref="SearchQuery.ResultLimit"/> plus one row (so a caller can tell
    /// whether more matched), projected by <paramref name="byDate"/> for a plain search or <paramref name="bySimilarity"/>
    /// for a search by similarity: by a region of a photo, whose vector comes from the region embedder (S-041), by description, whose vector comes from the embedder (throws
    /// <see cref="VisualSearchUnavailableException"/> without one), or to an asset, whose vector is its own stored
    /// embedding (throws <see cref="AssetNotAnalysedException"/> without one, S-017).
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
        await ResolvePeopleAsync(query.Filters, cancellationToken);
        var places = await ResolvePlacesAsync(query.Filters, cancellationToken);
        var limit = query.ResultLimit;
        float[] vector;
        double? minScore;
        if (query.Filters.SimilarTo is { } similar)
        {
            // The photo's own embedding (S-017, D-086), with no threshold: the most similar come back however close.
            vector = await PgvectorSimilarity.StoredEmbeddingAsync(db, similar.AssetId, cancellationToken);
            minScore = null;
        }
        else if (query.Filters.SimilarToRegion is { } region)
        {
            // The part of the photo the user marked (S-041, D-099), compared with every photo's embedding, no threshold.
            if (regionEmbedder is not { IsAvailable: true })
            {
                throw new VisualSearchUnavailableException("Search by a region is not available: the CLIP image model is not loaded.");
            }

            vector = await regionEmbedder.EmbedAsync(region.AssetId, region.Box, cancellationToken);
            minScore = null;
        }
        else if (query.Filters.Visual is { } visual)
        {
            if (embedder is not { IsAvailable: true })
            {
                throw new VisualSearchUnavailableException("Search by description is not available: the CLIP text model is not loaded.");
            }

            vector = embedder.Embed(visual.Text);
            minScore = visual.EffectiveMinScore;
        }
        else
        {
            return await byDate(Query(query, places)).Take(limit + 1).ToListAsync(cancellationToken);
        }

        // The index walk only for top results without other filters, on a pgvector that can walk it iteratively; with
        // filters, for counting, or on an older pgvector, every candidate is ranked exactly (D-070). Leaving the asset
        // itself out of a search for similar ones is not a filter that can hide matches from the walk: it is one row.
        var walkIndex = similarity == SimilarityMode.Ranking
            && (query.Filters with { Visual = null, SimilarTo = null, SimilarToRegion = null }).IsEmpty
            && await PgvectorSimilarity.CanWalkIndexAsync(db, cancellationToken);
        var ranked = PgvectorSimilarity.Rank(db, Filtered(query, places), vector, minScore, exact: !walkIndex);
        if (!walkIndex)
        {
            return await bySimilarity(ranked).Take(limit + 1).ToListAsync(cancellationToken);
        }

        // One transaction, so the index settings apply to this query only and leave the pooled connection as it was.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PgvectorSimilarity.PrepareIndexWalkAsync(db, cancellationToken);
        var rows = await bySimilarity(ranked).Take(limit + 1).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>
    /// Counts what the query matches (S-026, D-075), exactly, with plain EF Core grouping (D-051). Null for a query with
    /// a description: its matches depend on a similarity threshold, so no number is given in the MVP (D-070); and for
    /// one for similar photos, which are the most similar few, not a set to count (S-017).
    /// </summary>
    public async Task<CountResult?> CountAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Filters.Visual is not null || query.Filters.SimilarTo is not null || query.Filters.SimilarToRegion is not null)
        {
            return null;
        }

        var named = await ResolvePeopleAsync(query.Filters, cancellationToken);
        var places = await ResolvePlacesAsync(query.Filters, cancellationToken);
        var assets = Filtered(query, places);
        var aggregate = query.Aggregate ?? new SearchAggregate();
        if (aggregate.Count == SearchCount.Events)
        {
            return await CountEventsAsync(assets, aggregate, cancellationToken);
        }

        var summed = aggregate.Sum == SearchSum.Bytes;
        // The count and the sum in one pass: a filter such as the number of sources is the costly part of the query.
        int total;
        long? bytes = null;
        if (summed)
        {
            var totals = await assets.GroupBy(_ => 1).Select(g => new { Count = g.Count(), Bytes = g.Sum(a => a.SizeBytes) }).SingleOrDefaultAsync(cancellationToken);
            (total, bytes) = (totals?.Count ?? 0, totals?.Bytes ?? 0);
        }
        else
        {
            total = await assets.CountAsync(cancellationToken);
        }

        var groups = aggregate.GroupBy switch
        {
            null => [],
            SearchGroupBy.Year => Numbered(await assets.GroupBy(a => a.CapturedAt == null ? (int?)null : a.CapturedAt.Value.Year)
                .Select(g => new { g.Key, Count = g.Count(), Bytes = g.Sum(a => a.SizeBytes) }).ToListAsync(cancellationToken),
                g => (g.Key, g.Count, g.Bytes)),
            SearchGroupBy.Month => Numbered(await assets.GroupBy(a => a.CapturedAt == null ? (int?)null : a.CapturedAt.Value.Month)
                .Select(g => new { g.Key, Count = g.Count(), Bytes = g.Sum(a => a.SizeBytes) }).ToListAsync(cancellationToken),
                g => (g.Key, g.Count, g.Bytes)),
            SearchGroupBy.Kind => [.. (await assets.GroupBy(a => a.Kind)
                .Select(g => new { g.Key, Count = g.Count(), Bytes = g.Sum(a => a.SizeBytes) }).ToListAsync(cancellationToken))
                .Select(g => new CountGroup(g.Key, g.Count, g.Bytes))],
            SearchGroupBy.City => [.. (await assets.GroupBy(a => a.PlaceCity)
                .Select(g => new { g.Key, Count = g.Count(), Bytes = g.Sum(a => a.SizeBytes) }).ToListAsync(cancellationToken))
                .Select(g => new CountGroup(g.Key, g.Count, g.Bytes))],
            // An asset found on two drives counts once in each, with its whole size in each; twice on one drive, once.
            SearchGroupBy.Source => [.. (await assets.SelectMany(a => a.Sources.Select(s => new { a.Id, a.SizeBytes, s.Name })).Distinct()
                .GroupBy(x => x.Name).Select(g => new { g.Key, Count = g.Count(), Bytes = g.Sum(x => x.SizeBytes) }).ToListAsync(cancellationToken))
                .Select(g => new CountGroup(g.Key, g.Count, g.Bytes))],
            // Named people only, once per photo; the people the filters name are in every photo, so they are left out
            // ("who did I take the most photos with" is never "me", D-080).
            SearchGroupBy.Person => [.. (await db.Faces.AsNoTracking()
                .Where(f => (f.Assignment == FaceAssignments.Auto || f.Assignment == FaceAssignments.Manual)
                    && f.PersonId != null && !named.Contains(f.PersonId.Value) && f.Person!.Name != null
                    && assets.Select(a => a.Id).Contains(f.AssetId))
                .Select(f => new { f.AssetId, Name = f.Person!.Name! })
                .Distinct()
                .GroupBy(x => x.Name).Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
                .Select(g => new CountGroup(g.Key, g.Count))],
            _ => throw new InvalidOperationException($"Unknown groupBy '{aggregate.GroupBy}'."),
        };
        // Sizes are only given when asked for (S-028); the groups were summed in the same queries as their counts.
        if (!summed)
        {
            groups = [.. groups.Select(g => g with { Bytes = null })];
        }

        int? undated = query.Filters.HasTimeFilter
            ? await Filter(db.Assets.AsNoTracking(), query.Filters.UndatedInstead, places).CountAsync(cancellationToken)
            : null;
        // Without top, a grouping by year, month or kind is complete and in its own order ("photos per month" is 12
        // months, in order); by city or source, the largest 10. With top, the largest that many (D-075).
        var ordered = aggregate.Top is null && aggregate.GroupBy is SearchGroupBy.Year or SearchGroupBy.Month or SearchGroupBy.Kind
            ? InKeyOrder(groups).Take(SearchAggregate.MaxTop)
            : Largest(groups, bySize: summed).Take(aggregate.EffectiveTop);
        return new CountResult(total, [.. ordered], undated, groups.Count) { Bytes = bytes };
    }

    // The occasions with at least one matching asset, each once (S-024, D-094). Undated assets are in no occasion, so
    // a time filter leaves nothing out that could be counted.
    private async Task<CountResult> CountEventsAsync(IQueryable<Asset> assets, SearchAggregate aggregate, CancellationToken cancellationToken)
    {
        var matching = assets.Select(a => a.Id);
        var events = db.Occasions.AsNoTracking()
            .Where(o => db.OccasionAssets.Any(l => l.EventId == o.Id && matching.Contains(l.AssetId)));
        var total = await events.CountAsync(cancellationToken);
        if (aggregate.GroupBy is null)
        {
            return new CountResult(total, [], null);
        }

        var groups = (await events.GroupBy(o => o.Type).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .Select(g => new CountGroup(g.Key, g.Count)).ToList();
        return new CountResult(total, [.. Largest(groups).Take(aggregate.EffectiveTop)], null, groups.Count);
    }

    // Years and months in order, photos before videos, "no date" last.
    private static IEnumerable<CountGroup> InKeyOrder(IEnumerable<CountGroup> groups) =>
        groups.OrderBy(g => g.Key is null)
            .ThenBy(g => int.TryParse(g.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
            .ThenBy(g => g.Key, StringComparer.Ordinal);

    private static List<CountGroup> Numbered<T>(IEnumerable<T> groups, Func<T, (int? Key, int Count, long Bytes)> select) =>
        [.. groups.Select(select).Select(g => new CountGroup(g.Key?.ToString(CultureInfo.InvariantCulture), g.Count, g.Bytes))];

    // Largest first; between equal counts, years and months in order and names alphabetically, "none" last.
    // With sizes asked for (S-028), the largest is the one that takes up the most, then the one with the most items.
    private static IEnumerable<CountGroup> Largest(IEnumerable<CountGroup> groups, bool bySize = false) =>
        (bySize ? groups.OrderByDescending(g => g.Bytes ?? 0).ThenByDescending(g => g.Count) : groups.OrderByDescending(g => g.Count))
            .ThenBy(g => g.Key is null)
            .ThenBy(g => int.TryParse(g.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
            .ThenBy(g => g.Key, StringComparer.Ordinal);

    /// <summary>Today's date where the family is, for "on this day".</summary>
    public DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), options?.TimeZone ?? clock.LocalTimeZone).DateTime);

    private IQueryable<Asset> Filter(IQueryable<Asset> assets, SearchFilters filters, ResolvedPlaces places)
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

        if (filters.SavedPlaces is { } savedPlaces)
        {
            assets = PostgisDistance.InAny(assets, savedPlaces.Select(places.Named).DistinctBy(p => p.Id));
        }

        if (filters.DistanceFrom is { } distance)
        {
            var centre = distance.Home
                ? places.Home ?? throw new InvalidOperationException("Resolve the places with ResolvePlacesAsync before filtering by the distance from home.")
                : places.Named(distance.Place!);
            assets = PostgisDistance.Between(assets, centre.Location, distance.MinKm * 1000, distance.MaxKm * 1000);
        }

        if (filters.Sources is { } sources)
        {
            var lowered = sources.Select(s => s.ToLowerInvariant()).ToList();
            assets = assets.Where(a => a.Sources.Any(s => lowered.Contains(s.Name.ToLower())));
        }

        if (filters.SourceCount is { } sourceCount)
        {
            // Different source names, so a file found twice on one drive is in one source (as groupBy source counts it).
            // One grouped pass over the sources, joined to the assets, not a count per asset: a count per asset took
            // 1.6 s on 100,000 assets (D-093). An asset with no source has no row, and no count asks for 0.
            var counted = db.AssetSources.GroupBy(s => s.AssetId);
            var ids = sourceCount.Eq is { } exactly
                ? counted.Where(g => g.Select(s => s.Name).Distinct().Count() == exactly).Select(g => g.Key)
                : counted.Where(g => g.Select(s => s.Name).Distinct().Count() >= sourceCount.Gte!.Value).Select(g => g.Key);
            assets = assets.Where(a => ids.Contains(a.Id));
        }

        if (filters.People is { } people)
        {
            assets = WithPeople(assets, people);
        }

        if (filters.EventTypes is { } eventTypes)
        {
            assets = assets.Where(a => db.OccasionAssets.Any(l => l.AssetId == a.Id
                && db.Occasions.Any(o => o.Id == l.EventId && o.Type != null && eventTypes.Contains(o.Type))));
        }

        if (filters.EventIds is { } eventIds)
        {
            assets = assets.Where(a => db.OccasionAssets.Any(l => l.AssetId == a.Id && eventIds.Contains(l.EventId)));
        }

        // "More like this one" never shows this one (S-017).
        if (filters.SimilarTo is { } similar)
        {
            var self = similar.AssetId;
            assets = assets.Where(a => a.Id != self);
        }

        return assets;
    }

    // A place's name cleaned as it is stored (D-079); lower() is then applied in SQL.
    internal static string PlaceKey(string name)
    {
        PeopleCuration.CleanName(name, out var clean);
        return clean ?? name.Trim();
    }

    private sealed record PlaceMatch(string Name, long? Id);

    // A searched name cleaned as names are stored (D-079: NFC, invisible formatting dropped, white space collapsed), so a
    // double space or a decomposed "José" still finds the person, then lower-cased to match ignoring case.
    private static string NameKey(string name)
    {
        PeopleCuration.CleanName(name, out var clean);
        return (clean ?? name.Trim()).ToLowerInvariant();
    }

    // Every person named, and the user's own: each must have a counted face in the photo (D-077, D-079).
    private IQueryable<Asset> WithPeople(IQueryable<Asset> assets, PeopleFilter people)
    {
        var counted = db.Faces.Where(f => f.Assignment == FaceAssignments.Auto || f.Assignment == FaceAssignments.Manual);
        foreach (var name in people.All ?? [])
        {
            string[] lowered = [NameKey(name)];
            assets = assets.Where(a => counted.Any(f => f.AssetId == a.Id && f.Person!.Name != null && lowered.Contains(f.Person.Name.ToLower())));
        }

        if (people.IncludeMe)
        {
            // No address, no "me": nothing matches (ResolvePeopleAsync reports it before a search runs).
            var me = _me ?? string.Empty;
            assets = assets.Where(a => counted.Any(f => f.AssetId == a.Id && f.Person!.UserEmail == me));
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
