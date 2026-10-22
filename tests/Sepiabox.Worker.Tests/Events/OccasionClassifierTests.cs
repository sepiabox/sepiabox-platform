using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Pgvector;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Events;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Worker.Tests.Events;

/// <summary>Occasion types from the photos' embeddings, and trips (S-024). Synthetic vectors on made-up axes only.</summary>
public sealed class OccasionClassifierTests
{
    private static readonly DateTime s_start = new(2024, 12, 14, 10, 0, 0);
    private static readonly string[] s_keys = [.. OccasionTypes.Clip.Select(t => t.Key)];

    [Fact]
    public void Lists_the_seven_types_told_by_the_photos_and_trip_apart()
    {
        Assert.Equal(["wedding", "birthday", "beach", "christmas", "graduation", "snow", "party"], s_keys);
        Assert.Equal([.. s_keys, "trip"], OccasionTypes.All);
        Assert.Equal("a photo of a wedding", OccasionTypes.Clip[0].Prompt);
        Assert.All(OccasionTypes.Clip, t => Assert.StartsWith("a photo", t.Prompt, StringComparison.Ordinal));
        Assert.True(OccasionTypes.IsKnown("trip"));
        Assert.False(OccasionTypes.IsKnown("funeral"));
    }

    [Fact]
    public void An_occasion_with_wedding_photos_in_60_percent_of_them_is_a_wedding()
    {
        var photos = Photos(wedding: 6, other: 4);

        Assert.Equal(0, OccasionClassifier.Classify(photos, Labels(), distractors: 1));
    }

    [Fact]
    public void An_occasion_where_no_type_reaches_30_percent_of_the_photos_has_none()
    {
        // Two wedding photos in ten: 20%.
        Assert.Null(OccasionClassifier.Classify(Photos(wedding: 2, other: 8), Labels(), distractors: 1));
        // Three in ten: exactly 30%, enough.
        Assert.Equal(0, OccasionClassifier.Classify(Photos(wedding: 3, other: 7), Labels(), distractors: 1));
    }

    [Fact]
    public void The_type_with_the_most_photos_wins_and_the_first_in_the_file_wins_a_tie()
    {
        var beach = Axis(2);
        var snow = Axis(5);
        var photos = new List<float[]> { beach, beach, beach, snow, snow, snow, snow };
        Assert.Equal(5, OccasionClassifier.Classify(photos, Labels(), distractors: 1));

        Assert.Equal(2, OccasionClassifier.Classify([beach, beach, snow, snow], Labels(), distractors: 1));
    }

    [Fact]
    public void A_photo_that_no_type_is_clearly_about_counts_for_none()
    {
        // Equally close to all seven: a probability of 1/7, below 0.35.
        Assert.Null(OccasionClassifier.Classify([Axis(100), Axis(100)], Labels(), distractors: 1));
    }

    [Fact]
    public void A_photo_between_two_types_counts_for_both()
    {
        var between = Normalize(Axis(0), Axis(1));

        Assert.Equal(0, OccasionClassifier.Classify([between], Labels(), distractors: 1));
        var photos = new List<float[]> { between, between, Axis(1) };
        // Birthday: three photos; wedding: two.
        Assert.Equal(1, OccasionClassifier.Classify(photos, Labels(), distractors: 1));
    }

    [Fact]
    public void A_photo_the_neutral_prompt_also_describes_counts_for_no_type()
    {
        var neutral = Axis(s_keys.Length);
        // Equally a wedding, a birthday and "a photo": a third each, below 0.35.
        var vague = Normalize(Normalize(Axis(0), Axis(1)), neutral);

        Assert.Null(OccasionClassifier.Classify([vague, vague, vague], Labels(), distractors: 1));
        // Without the neutral prompt in the softmax, it is a wedding or a birthday at 0.5 each: the first in the file wins.
        Assert.Equal(0, OccasionClassifier.Classify([vague, vague, vague], Labels()[..^1]));
        // A photo on the neutral prompt alone is of no type.
        Assert.Null(OccasionClassifier.Classify([neutral, neutral, neutral], Labels(), distractors: 1));
    }

    [Fact]
    public void Exactly_30_percent_is_enough_in_whole_numbers()
    {
        Assert.Equal(0, OccasionClassifier.Classify(Photos(wedding: 3, other: 7), Labels(), distractors: 1));
        Assert.Null(OccasionClassifier.Classify(Photos(wedding: 29, other: 71), Labels(), distractors: 1));
        Assert.Equal(0, OccasionClassifier.Classify(Photos(wedding: 30, other: 70), Labels(), distractors: 1));
    }

    [Fact]
    public void An_occasion_without_photos_that_have_embeddings_has_no_type()
    {
        Assert.Null(OccasionClassifier.Classify([], Labels(), distractors: 1));
    }

    [Fact]
    public void Refuses_embeddings_of_another_size_than_the_types()
    {
        Assert.Throws<InvalidOperationException>(() => OccasionClassifier.Classify([new float[3]], Labels()));
    }

    [Theory]
    [InlineData(2, 300, true)]
    [InlineData(1, 300, true)]
    [InlineData(0.5, 300, false)]
    [InlineData(2, 99, false)]
    [InlineData(2, 101, true)]
    public void A_trip_lasts_a_day_or_more_and_is_over_100_km_from_home(double days, double kilometres, bool trip)
    {
        var home = new Point(-9.14, 38.72) { SRID = 4326 };
        var occasion = new Occasion
        {
            StartsAt = s_start,
            EndsAt = s_start.AddDays(days),
            Centroid = new Point(-9.14, 38.72 + kilometres / 111.195) { SRID = 4326 },
        };

        Assert.Equal(trip, OccasionClassifier.IsTrip(occasion, home));
    }

    [Fact]
    public void Without_a_home_place_or_a_centre_nothing_is_a_trip()
    {
        var far = new Occasion { StartsAt = s_start, EndsAt = s_start.AddDays(3), Centroid = new Point(0, 0) { SRID = 4326 } };

        Assert.False(OccasionClassifier.IsTrip(far, null));
        Assert.False(OccasionClassifier.IsTrip(far.With0Centre(), new Point(-9.14, 38.72) { SRID = 4326 }));
    }

    internal static float[] Axis(int axis)
    {
        var vector = new float[512];
        vector[axis] = 1;
        return vector;
    }

    // The types' axes, then the neutral prompt's (D-094).
    private static List<float[]> Labels() => [.. s_keys.Select((_, i) => Axis(i)), Axis(s_keys.Length)];

    // Wedding photos on the wedding axis, the others on one that is none of the types'.
    private static List<float[]> Photos(int wedding, int other) =>
        [.. Enumerable.Repeat(Axis(0), wedding), .. Enumerable.Repeat(Axis(100), other)];

    private static float[] Normalize(float[] a, float[] b)
    {
        var sum = a.Zip(b, (x, y) => x + y).ToArray();
        var norm = MathF.Sqrt(sum.Sum(v => v * v));
        return [.. sum.Select(v => v / norm)];
    }
}

file static class OccasionExtensions
{
    public static Occasion With0Centre(this Occasion occasion) => new() { StartsAt = occasion.StartsAt, EndsAt = occasion.EndsAt };
}

/// <summary>Maps each type's prompt to its own axis, so a photo on an axis is a photo of that type.</summary>
internal sealed class TypeAxisEmbedder : IQueryEmbedder
{
    public bool IsAvailable => true;

    public List<string> Embedded { get; } = [];

    public float[] Embed(string text)
    {
        Embedded.Add(text);
        if (text == OccasionTypes.DistractorPrompt)
        {
            return OccasionClassifierTests.Axis(OccasionTypes.Clip.Count);
        }

        var index = OccasionTypes.Clip.ToList().FindIndex(t => t.Prompt == text);
        return index < 0 ? throw new InvalidOperationException($"Unknown prompt '{text}'.") : OccasionClassifierTests.Axis(index);
    }
}

/// <summary><c>events classify</c> on a real PostgreSQL catalog (S-024).</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class OccasionClassifyTests(CatalogFixture fixture)
{
    private static readonly DateTime s_start = new(2024, 12, 14, 10, 0, 0);

    [Fact]
    public async Task Gives_a_type_to_the_occasions_without_one_and_keeps_the_ones_that_have_one()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var wedding = await OccasionAsync(db, 1, "wedding axis", 6, 4);
        var plain = await OccasionAsync(db, 20, "none", 2, 8);
        var kept = await OccasionAsync(db, 40, "kept", 0, 10, type: "party");
        var embedder = new TypeAxisEmbedder();

        var result = await new OccasionClassifier(db, embedder).RunAsync(CancellationToken.None);

        Assert.Equal(new OccasionClassification(2, 1, 0, 1), result);
        Assert.Equal(8, embedder.Embedded.Count);
        var types = await db.Occasions.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Type);
        Assert.Equal(("wedding", null, "party"), (types[wedding], types[plain], types[kept]));
    }

    [Fact]
    public async Task A_two_day_occasion_300_km_from_the_home_place_is_a_trip_and_one_without_a_home_is_not()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var trip = await OccasionAsync(db, 1, "far", 0, 3, days: 2, latitude: 38.72 + 300 / 111.195);
        var near = await OccasionAsync(db, 10, "near", 0, 3, days: 2, latitude: 38.75);
        var result = await new OccasionClassifier(db, new TypeAxisEmbedder()).RunAsync(CancellationToken.None);
        Assert.Equal(new OccasionClassification(2, 0, 0, 2), result);

        db.SavedPlaces.Add(new SavedPlace { Name = "Home", Location = new Point(-9.14, 38.72) { SRID = 4326 }, RadiusM = 200, IsHome = true });
        await db.SaveChangesAsync();
        var again = await new OccasionClassifier(db, new TypeAxisEmbedder()).RunAsync(CancellationToken.None);

        Assert.Equal(new OccasionClassification(2, 0, 1, 1), again);
        var types = await db.Occasions.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Type);
        Assert.Equal(("trip", null), (types[trip], types[near]));
    }

    [Fact]
    public async Task The_photos_win_over_a_trip_and_a_second_run_changes_nothing()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        db.SavedPlaces.Add(new SavedPlace { Name = "Home", Location = new Point(-9.14, 38.72) { SRID = 4326 }, RadiusM = 200, IsHome = true });
        var abroad = await OccasionAsync(db, 1, "wedding abroad", 5, 0, days: 3, latitude: 45);

        var first = await new OccasionClassifier(db, new TypeAxisEmbedder()).RunAsync(CancellationToken.None);
        var second = await new OccasionClassifier(db, new TypeAxisEmbedder()).RunAsync(CancellationToken.None);

        Assert.Equal(new OccasionClassification(1, 1, 0, 0), first);
        Assert.Equal(new OccasionClassification(0, 0, 0, 0), second);
        Assert.Equal("wedding", (await db.Occasions.AsNoTracking().SingleAsync(o => o.Id == abroad)).Type);
    }

    [Fact]
    public async Task A_rebuild_clears_the_type_of_an_occasion_that_changed_and_classifying_gives_it_one_again()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var id = await OccasionAsync(db, 1, "wedding", 3, 0, type: "party");
        await db.Assets.AddAsync(Asset(99, s_start.AddHours(1)));
        await db.SaveChangesAsync();
        db.AssetEmbeddings.Add(new AssetEmbedding { AssetId = Id(99), Model = PgvectorSimilarity.ModelKey, Embedding = new Vector(OccasionClassifierTests.Axis(0)) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        Assert.Null((await db.Occasions.AsNoTracking().SingleAsync(o => o.Id == id)).Type);
        await new OccasionClassifier(db, new TypeAxisEmbedder()).RunAsync(CancellationToken.None);

        Assert.Equal("wedding", (await db.Occasions.AsNoTracking().SingleAsync(o => o.Id == id)).Type);
    }

    [Fact]
    public async Task A_text_model_that_is_missing_fails_the_run_before_any_occasion_is_touched()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await OccasionAsync(db, 1, "any", 3, 0);

        await Assert.ThrowsAsync<VisualSearchUnavailableException>(() =>
            new OccasionClassifier(db, new MissingModel()).RunAsync(CancellationToken.None));

        Assert.Null((await db.Occasions.AsNoTracking().SingleAsync()).Type);
    }

    // An occasion of assets numbered from `first`, each an hour after the last: the first `wedding` of them with an embedding
    // on the wedding axis, the next `other` on an axis that is none of the types'.
    private static async Task<long> OccasionAsync(
        CatalogDbContext db, int first, string label, int wedding, int other, string? type = null, double days = 0, double? latitude = null)
    {
        var count = wedding + other;
        var assets = Enumerable.Range(first, Math.Max(count, 1)).ToList();
        foreach (var (n, i) in assets.Select((n, i) => (n, i)))
        {
            db.Assets.Add(Asset(n, s_start.AddHours(i)));
        }

        await db.SaveChangesAsync();
        foreach (var (n, i) in assets.Select((n, i) => (n, i)).Take(count))
        {
            db.AssetEmbeddings.Add(new AssetEmbedding
            {
                AssetId = Id(n),
                Model = PgvectorSimilarity.ModelKey,
                Embedding = new Vector(OccasionClassifierTests.Axis(i < wedding ? 0 : 100)),
            });
        }

        var occasion = new Occasion
        {
            StartsAt = s_start,
            EndsAt = s_start.AddDays(days).AddHours(Math.Max(count - 1, 0)),
            AssetCount = assets.Count,
            PlaceLabel = label,
            Type = type,
            Centroid = latitude is { } lat ? new Point(-9.14, lat) { SRID = 4326 } : null,
        };
        db.Occasions.Add(occasion);
        await db.SaveChangesAsync();
        db.OccasionAssets.AddRange(assets.Select(n => new OccasionAsset { EventId = occasion.Id, AssetId = Id(n) }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return occasion.Id;
    }

    private static string Id(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

    private static Asset Asset(int n, DateTime at) => new()
    {
        Id = Id(n),
        Kind = "photo",
        ContentType = "image/jpeg",
        SizeBytes = 1000,
        CapturedAt = at,
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class MissingModel : IQueryEmbedder
    {
        public bool IsAvailable => false;

        public float[] Embed(string text) => throw new VisualSearchUnavailableException("The CLIP text model is not in the folder.");
    }
}

/// <summary>A fact that needs the CLIP text model: skipped when it is not downloaded, unless CI requires it.</summary>
internal sealed class ClipTextFactAttribute : FactAttribute
{
    public ClipTextFactAttribute()
    {
        if (!Sepiabox.Search.Clip.ClipTextEncoder.FilesPresent(TestModels.Folder) && !TestModels.Required)
        {
            Skip = $"The CLIP text model is not in {TestModels.Folder}; run `sepiabox-ingest models download`.";
        }
    }
}

/// <summary>The types' prompts with the real CLIP text model (S-024): each prompt is its own type, and a street is none.</summary>
[Trait("Category", "Integration")]
public sealed class OccasionTypesClipTests
{
    [ClipTextFact]
    public void Each_prompt_s_own_embedding_classifies_as_its_type_and_a_street_as_none()
    {
        using var encoder = Sepiabox.Search.Clip.ClipTextEncoder.TryCreate(TestModels.Folder, out var warning)
            ?? throw new InvalidOperationException(warning);
        var labels = new List<float[]>([.. OccasionTypes.Clip.Select(t => encoder.Encode(t.Prompt)), encoder.Encode(OccasionTypes.DistractorPrompt)]);

        for (var i = 0; i < OccasionTypes.Clip.Count; i++)
        {
            Assert.Equal(i, OccasionClassifier.Classify([labels[i]], labels, distractors: 1));
        }

        // Things no type describes: the neutral prompt itself, and a street.
        foreach (var prompt in new[] { "a photo of a street", OccasionTypes.DistractorPrompt })
        {
            Assert.Null(OccasionClassifier.Classify([encoder.Encode(prompt)], labels, distractors: 1));
        }
    }
}
