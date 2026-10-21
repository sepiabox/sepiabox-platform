using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The golden questions of S-025 (<c>TestData/questions.golden.json</c>): each question in English and Brazilian
/// Portuguese with the DSL it means on a fixed day, and a small fixture catalog to run both on, so two queries are
/// compared by what they find rather than by their JSON.
/// </summary>
internal sealed class GoldenQuestions
{
    public const string FileName = "questions.golden.json";
    public const string RecordedFileName = "questions.recorded.json";

    private static readonly Dictionary<string, string> s_countryCodes = new(StringComparer.Ordinal)
    {
        ["Portugal"] = "PT",
        ["Brazil"] = "BR",
        ["Spain"] = "ES",
    };

    private GoldenQuestions(JsonNode root)
    {
        Today = DateOnly.ParseExact(root["today"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        TimeZone = root["timeZone"]!.GetValue<string>();
        MeName = root["me"]!["name"]!.GetValue<string>();
        MeEmail = root["me"]!["email"]!.GetValue<string>();
        SavedPlaces = [.. root["savedPlaces"]!.AsArray().Select(p => new GoldenPlace(
            p!["name"]!.GetValue<string>(),
            p["lat"]!.GetValue<double>(),
            p["lon"]!.GetValue<double>(),
            p["radiusM"]!.GetValue<int>(),
            p["home"]!.GetValue<bool>()))];
        Concepts = root["visualConcepts"]!.AsObject()
            .Select((p, i) => new Concept(p.Key, i, [.. p.Value!.AsArray().Select(k => k!.GetValue<string>())]))
            .ToList();
        Catalog = [.. root["catalog"]!.AsArray().Select(a => new GoldenAsset(
            a!["n"]!.GetValue<int>(),
            a["kind"]!.GetValue<string>(),
            a["date"] is JsonValue date ? DateOnly.ParseExact(date.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            a["city"]?.GetValue<string>(),
            a["country"]?.GetValue<string>(),
            a["source"]!.GetValue<string>(),
            [.. a["concepts"]!.AsArray().Select(c => c!.GetValue<string>())],
            a["people"] is JsonArray people ? [.. people.Select(p => p!.GetValue<string>())] : [])
        {
            Location = a["location"] is JsonArray location ? (location[0]!.GetValue<double>(), location[1]!.GetValue<double>()) : null,
        })];
        Questions = [.. root["questions"]!.AsArray().Select(q => new GoldenQuestion(
            q!["id"]!.GetValue<string>(),
            q["en"]!.GetValue<string>(),
            q["pt"]!.GetValue<string>(),
            q["expected"]!.ToJsonString(),
            [.. q["covers"]!.AsArray().Select(c => c!.GetValue<string>())]))];
    }

    public DateOnly Today { get; }

    public string TimeZone { get; }

    /// <summary>The signed-in user's person ("This is me", S-021) and address (example.com).</summary>
    public string MeName { get; }

    public string MeEmail { get; }

    public IReadOnlyList<Concept> Concepts { get; }

    /// <summary>The family's own places (S-014), with made-up coordinates; one is the home.</summary>
    public IReadOnlyList<GoldenPlace> SavedPlaces { get; }

    public IReadOnlyList<GoldenAsset> Catalog { get; }

    public IReadOnlyList<GoldenQuestion> Questions { get; }

    /// <summary>Noon on the golden day in its time zone, so "today" is the same wherever the test runs.</summary>
    public TimeProvider Clock => new FixedTime(new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), Zone.GetUtcOffset(Today.ToDateTime(new TimeOnly(12, 0)))));

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    public static string PathOf(string fileName) => Path.Combine(SourceTree.Root, "tests", "Sepiabox.Web.Tests", "TestData", fileName);

    public static GoldenQuestions Load() => new(JsonNode.Parse(File.ReadAllText(PathOf(FileName)))!);

    /// <summary>The context the web would build for this catalog: the golden day, the sources, cities and country codes.</summary>
    public QuestionContext Context() => QuestionContext.For(Today, TimeZone)
        .Add("sources", "filters.sources", Catalog.Select(a => a.Source))
        .Add("cities", "filters.places.cities", Catalog.Where(a => a.City is not null).Select(a => a.City!))
        .Add("countries", "filters.places.countries", Catalog.Where(a => a.Country is not null).Select(a => s_countryCodes[a.Country!]))
        .Add("saved places", "filters.savedPlaces", SavedPlaces.Select(p => p.Name))
        .Add("home place", "filters.savedPlaces", SavedPlaces.Where(p => p.Home).Select(p => p.Name))
        .Add("people", "filters.people.all", Catalog.SelectMany(a => a.People))
        .Build();

    /// <summary>A migrated catalog holding the fixture's assets, their embeddings and the GeoNames rows of their places.</summary>
    public async Task<CatalogDbContext> SeedAsync(CatalogFixture catalog)
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        foreach (var asset in Catalog)
        {
            db.Assets.Add(new Asset
            {
                Id = Ids.Of(asset.N),
                Kind = asset.Kind,
                ContentType = asset.Kind == "video" ? "video/mp4" : "image/jpeg",
                SizeBytes = 1000,
                CapturedAt = asset.Date?.ToDateTime(new TimeOnly(12, 0)),
                PlaceCity = asset.City,
                Location = asset.Location is var (latitude, longitude) ? new Point(longitude, latitude) { SRID = 4326 } : null,
                PlaceCountry = asset.Country,
                ImportedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch,
                Sources = [new AssetSource { AssetId = Ids.Of(asset.N), Type = "local", Name = asset.Source, Path = $"{asset.N}.jpg" }],
            });
            // Photos have CLIP embeddings: along their concepts, or along an axis of their own, which no description matches.
            if (asset.Kind == "photo")
            {
                db.AssetEmbeddings.Add(new AssetEmbedding
                {
                    AssetId = Ids.Of(asset.N),
                    Model = PgvectorSimilarity.ModelKey,
                    Embedding = new Pgvector.Vector(Vector(asset.Concepts.Count == 0
                        ? [100 + asset.N]
                        : [.. asset.Concepts.Select(c => Concepts.Single(k => k.Name == c).Axis)])),
                });
            }
        }

        // Each name a person, and the user's own linked to them; every face counted (auto), with a synthetic vector.
        var people = Catalog.SelectMany(a => a.People).Distinct().ToDictionary(
            name => name,
            name => new Person { Name = name, UserEmail = name == MeName ? MeEmail : null, FaceCount = Catalog.Count(a => a.People.Contains(name)) });
        db.People.AddRange(people.Values);
        foreach (var asset in Catalog)
        {
            foreach (var (name, i) in asset.People.Select((name, i) => (name, i)))
            {
                var embedding = new float[128];
                embedding[i] = 1;
                db.Faces.Add(new Face
                {
                    AssetId = Ids.Of(asset.N),
                    Box = [0.1 * i, 0.1, 0.1, 0.1],
                    Score = 0.9,
                    Embedding = new Pgvector.Vector(embedding),
                    Person = people[name],
                    Assignment = FaceAssignments.Auto,
                });
            }
        }

        foreach (var place in SavedPlaces)
        {
            db.SavedPlaces.Add(new SavedPlace
            {
                Name = place.Name,
                Location = new Point(place.Longitude, place.Latitude) { SRID = 4326 },
                RadiusM = place.RadiusM,
                IsHome = place.Home,
            });
        }

        var id = 1L;
        foreach (var (city, country) in Catalog.Where(a => a.City is not null).Select(a => (a.City!, a.Country!)).Distinct())
        {
            db.GeoNames.Add(new GeoName
            {
                GeonameId = id++,
                Name = city,
                AsciiName = Ascii(city),
                CountryCode = s_countryCodes[country],
                CountryName = country,
                Population = 1000,
                Location = new Point(0, 0) { SRID = 4326 },
            });
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    /// <summary>The ids a DSL query finds in the fixture catalog, every match ranked exactly (D-070).</summary>
    public async Task<List<string>> FindAsync(CatalogDbContext db, string json)
    {
        if (!SearchQueryJson.TryParse(json, out var query, out var error))
        {
            throw new InvalidOperationException($"Not a valid query: {error}");
        }

        var search = new SearchService(db, Clock, new SearchOptions { TimeZone = Zone }, new ConceptEmbedder(Concepts), MeEmail);
        // A count is compared by its numbers: the total, the undated it leaves out, and the groups in order (S-026).
        if (query.Aggregate is not null && await search.CountAsync(query) is { } count)
        {
            return ["count", $"total={count.Total}", $"undated={count.Undated}", .. count.Groups.Select(g => $"{g.Key ?? "(none)"}={g.Count}")];
        }

        var result = await search.ExecuteAsync(query, SimilarityMode.Exact);
        // A visual search ranks by similarity: ties between equally similar assets may come in any order.
        return query.Filters.Visual is null && query.Limit < Catalog.Count
            ? [.. result.Hits.Select(h => h.Id)]
            : [.. result.Hits.Select(h => h.Id).Order(StringComparer.Ordinal)];
    }

    private static float[] Vector(IReadOnlyList<int> axes)
    {
        var vector = new float[512];
        foreach (var axis in axes)
        {
            vector[axis] = 1;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        return [.. vector.Select(v => v / norm)];
    }

    private static string Ascii(string name) => new([.. name.Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);

    public sealed record Concept(string Name, int Axis, IReadOnlyList<string> Keywords);

    public sealed record GoldenAsset(
        int N, string Kind, DateOnly? Date, string? City, string? Country, string Source, IReadOnlyList<string> Concepts, IReadOnlyList<string> People)
    {
        /// <summary>Where it was taken (S-014), made up; null for most assets.</summary>
        public (double Latitude, double Longitude)? Location { get; init; }
    }

    public sealed record GoldenPlace(string Name, double Latitude, double Longitude, int RadiusM, bool Home);

    public sealed record GoldenQuestion(string Id, string English, string Portuguese, string Expected, IReadOnlyList<string> Covers)
    {
        public IEnumerable<(string Key, string Text)> InBothLanguages() => [($"{Id}/en", English), ($"{Id}/pt", Portuguese)];
    }

    /// <summary>
    /// Stands in for CLIP's text side: an English description lands on the axes of the concepts whose keywords it
    /// contains, so "snowy mountains" finds both snow and mountains, and anything else finds nothing.
    /// </summary>
    private sealed class ConceptEmbedder(IReadOnlyList<Concept> concepts) : IQueryEmbedder
    {
        public bool IsAvailable => true;

        public float[] Embed(string text)
        {
            var lower = text.ToLowerInvariant();
            var axes = concepts.Where(c => c.Keywords.Any(k => lower.Contains(k, StringComparison.Ordinal))).Select(c => c.Axis).ToList();
            return Vector(axes.Count == 0 ? [511] : axes);
        }
    }
}
