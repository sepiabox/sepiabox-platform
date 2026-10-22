using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Catalog.Events;

/// <summary>What <see cref="OccasionClassifier"/> did: how many occasions it looked at and what it decided.</summary>
public sealed record OccasionClassification(int Considered, int ByPhotos, int Trips, int Untyped);

/// <summary>
/// <c>events classify</c> (S-024, D-019, D-094): gives a type to every occasion that has none, including those a rebuild
/// cleared because their assets changed. By the photos: each photo's CLIP embedding is compared with the text embedding
/// of every type's prompt, a softmax with temperature 100 gives it a probability for each, and the type is the one with
/// the most photos at probability 0.35 or more, if those are at least 30% of the occasion's photos with an embedding
/// (the first in the file when tied). The softmax also holds a neutral prompt (<see cref="OccasionTypes.DistractorPrompt"/>)
/// that is never a type, so a photo that none of the types describes does not count for the least wrong one. When no type wins, an occasion more than 100 km from the place marked as home and
/// lasting a day or more is a <c>trip</c>; without a home place nothing is. A type from the photos wins over a trip: a
/// wedding abroad is a wedding.
/// </summary>
public sealed class OccasionClassifier(CatalogDbContext db, IQueryEmbedder embedder)
{
    public const double Temperature = 100;
    public const double MinProbability = 0.35;
    public const double TripKm = 100;

    public async Task<OccasionClassification> RunAsync(CancellationToken cancellationToken)
    {
        // Embedded once, before anything is read: a text model that is not there fails the run at once.
        var labels = OccasionTypes.Clip.Select(t => embedder.Embed(t.Prompt)).Append(embedder.Embed(OccasionTypes.DistractorPrompt)).ToList();
        var home = await db.SavedPlaces.AsNoTracking().Where(p => p.IsHome).Select(p => p.Location).FirstOrDefaultAsync(cancellationToken);

        // Under the rebuild's lock, in one transaction: a rebuild that runs meanwhile would clear or delete the occasions
        // being typed, and the two would overwrite each other.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({OccasionRebuild.AdvisoryLockKey})", cancellationToken);
        var occasions = await db.Occasions.Where(o => o.Type == null).OrderBy(o => o.Id).ToListAsync(cancellationToken);
        int byPhotos = 0, trips = 0;
        foreach (var occasion in occasions)
        {
            var photos = await (
                from link in db.OccasionAssets
                where link.EventId == occasion.Id
                join e in db.AssetEmbeddings on link.AssetId equals e.AssetId
                where e.Model == PgvectorSimilarity.ModelKey
                select e.Embedding).ToListAsync(cancellationToken);
            var type = Classify([.. photos.Select(p => p.ToArray())], labels, distractors: 1) is { } index ? OccasionTypes.Clip[index].Key : null;
            if (type is not null)
            {
                byPhotos++;
            }
            else if (IsTrip(occasion, home))
            {
                type = OccasionTypes.Trip;
                trips++;
            }

            occasion.Type = type;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OccasionClassification(occasions.Count, byPhotos, trips, occasions.Count - byPhotos - trips);
    }

    /// <summary>
    /// The index of the winning label for these photo embeddings, or null. Photos and labels are L2-normalized, so the
    /// dot product is the cosine similarity. The last <paramref name="distractors"/> labels take part in the softmax but
    /// never win.
    /// </summary>
    public static int? Classify(IReadOnlyList<float[]> photos, IReadOnlyList<float[]> labels, int distractors = 0)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(labels);
        if (photos.Count == 0 || labels.Count == 0)
        {
            return null;
        }

        var types = labels.Count - distractors;
        var counts = new int[types];
        var logits = new double[labels.Count];
        foreach (var photo in photos)
        {
            for (var j = 0; j < labels.Count; j++)
            {
                logits[j] = Temperature * Dot(photo, labels[j]);
            }

            // Shifted by the largest, so exp never overflows.
            var max = logits.Max();
            var sum = logits.Sum(l => Math.Exp(l - max));
            for (var j = 0; j < types; j++)
            {
                if (Math.Exp(logits[j] - max) / sum >= MinProbability)
                {
                    counts[j]++;
                }
            }
        }

        var best = Array.IndexOf(counts, counts.Max());
        // At least 30% of the photos, in whole numbers, so 3 in 10 is exactly 30%.
        return counts[best] * 10 >= photos.Count * 3 ? best : null;
    }

    /// <summary>Whether an occasion is a trip: a day or more, and its centre more than 100 km from home.</summary>
    public static bool IsTrip(Occasion occasion, NetTopologySuite.Geometries.Point? home) =>
        home is not null
        && occasion.Centroid is { } centre
        && occasion.EndsAt - occasion.StartsAt >= TimeSpan.FromDays(1)
        && EventBuilder.DistanceKm(home.Y, home.X, centre.Y, centre.X) > TripKm;

    private static double Dot(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new InvalidOperationException($"An embedding has {a.Length} values and a type's has {b.Length}.");
        }

        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}
