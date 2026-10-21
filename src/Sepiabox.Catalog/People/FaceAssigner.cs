using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Sepiabox.Catalog.People;

/// <summary>
/// <c>People:SameFaceThreshold</c>: the cosine similarity from which a face joins the nearest person (S-019), 0.40 by
/// default, SFace's published same-person threshold for these embeddings (D-016, D-047).
/// </summary>
public sealed record FaceAssignerOptions
{
    public const double DefaultThreshold = 0.40;

    public double Threshold { get; init; } = DefaultThreshold;

    public static FaceAssignerOptions For(double threshold) =>
        threshold is > 0 and <= 1
            ? new FaceAssignerOptions { Threshold = threshold }
            : throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "People:SameFaceThreshold must be above 0 and at most 1.");
}

/// <summary>A face read from a manifest: a valid box and a finite, non-zero embedding of 128 values.</summary>
public sealed record IncomingFace(double[] Box, double Score, float[] Embedding);

/// <summary>
/// Puts an asset's faces into people as it is imported (S-019, D-077): stores the manifest's faces in place of the
/// asset's earlier ones, keeping the family's curation of each face found again, gives every new face the person whose
/// centroid is nearest when it is similar enough or a new person otherwise, and recomputes the centroid and face count
/// of every person it touched. It only changes the tracked context: the caller saves, so the asset and its faces commit
/// together.
/// </summary>
public sealed class FaceAssigner(CatalogDbContext db, FaceAssignerOptions? options = null)
{
    /// <summary>A stored face is the same face as an incoming one when their boxes overlap this much (D-047).</summary>
    public const double SameBoxIoU = 0.5;

    private readonly double _threshold = (options ?? new FaceAssignerOptions()).Threshold;

    /// <summary>
    /// Replaces the faces of <paramref name="assetId"/> with <paramref name="incoming"/>, found by face pipeline
    /// <paramref name="version"/>. Returns false, changing nothing, when the stored faces come from a newer pipeline
    /// (<paramref name="storedVersion"/>), which a manifest of an older one never replaces.
    /// </summary>
    public async Task<bool> ApplyAsync(
        string assetId, int? storedVersion, int version, IReadOnlyList<IncomingFace> incoming, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (storedVersion > version)
        {
            return false;
        }

        var stored = await db.Faces.Where(f => f.AssetId == assetId).ToListAsync(cancellationToken);
        var touchedIds = stored.Where(f => f.PersonId is not null).Select(f => f.PersonId!.Value).ToHashSet();
        var created = new List<Person>();
        var kept = new List<Face>();

        var matches = Match(stored.Select(f => f.Box).ToList(), incoming.Select(f => f.Box).ToList());
        for (var i = 0; i < incoming.Count; i++)
        {
            var face = incoming[i];
            if (matches.TryGetValue(i, out var storedIndex))
            {
                // The same face found again: new numbers, the same person and assignment.
                var row = stored[storedIndex];
                row.Box = face.Box;
                row.Score = face.Score;
                row.Embedding = new Vector(face.Embedding);
                kept.Add(row);
                continue;
            }

            var person = await NearestAsync(face.Embedding, version, created, kept, cancellationToken);
            if (person is null)
            {
                person = new Person { FacesVersion = version };
                db.People.Add(person);
                created.Add(person);
            }
            else if (person.Id != 0)
            {
                touchedIds.Add(person.Id);
            }

            var added = new Face
            {
                AssetId = assetId,
                Box = face.Box,
                Score = face.Score,
                Embedding = new Vector(face.Embedding),
                Person = person,
                PersonId = person.Id == 0 ? null : person.Id,
                Assignment = FaceAssignments.Auto,
            };
            db.Faces.Add(added);
            kept.Add(added);
        }

        var matched = matches.Values.ToHashSet();
        for (var i = 0; i < stored.Count; i++)
        {
            if (!matched.Contains(i))
            {
                db.Faces.Remove(stored[i]);
            }
        }

        foreach (var id in touchedIds)
        {
            var person = await db.People.FindAsync([id], cancellationToken);
            if (person is not null)
            {
                await RecomputeAsync(person, assetId, version, kept, cancellationToken);
            }
        }

        foreach (var person in created)
        {
            await RecomputeAsync(person, assetId, version, kept, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// The index of the centroid most similar to <paramref name="embedding"/> when its cosine similarity reaches
    /// <paramref name="threshold"/>; null when none does (the face then starts a new person).
    /// </summary>
    public static int? Choose(IReadOnlyList<float[]> centroids, float[] embedding, double threshold)
    {
        ArgumentNullException.ThrowIfNull(centroids);
        int? best = null;
        var bestSimilarity = double.NegativeInfinity;
        for (var i = 0; i < centroids.Count; i++)
        {
            var similarity = FaceMath.Cosine(centroids[i], embedding);
            if (similarity >= threshold && similarity > bestSimilarity)
            {
                best = i;
                bestSimilarity = similarity;
            }
        }

        return best;
    }

    /// <summary>
    /// Pairs incoming boxes with stored ones (incoming index to stored index): the pairs that overlap by at least
    /// <see cref="SameBoxIoU"/>, largest overlap first, each box used once.
    /// </summary>
    public static Dictionary<int, int> Match(IReadOnlyList<double[]> stored, IReadOnlyList<double[]> incoming)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(incoming);
        var pairs = new List<(int Incoming, int Stored, double IoU)>();
        for (var i = 0; i < incoming.Count; i++)
        {
            for (var s = 0; s < stored.Count; s++)
            {
                var iou = FaceMath.IoU(incoming[i], stored[s]);
                if (iou >= SameBoxIoU)
                {
                    pairs.Add((i, s, iou));
                }
            }
        }

        var result = new Dictionary<int, int>();
        var used = new HashSet<int>();
        foreach (var (i, s, _) in pairs.OrderByDescending(p => p.IoU).ThenBy(p => p.Incoming).ThenBy(p => p.Stored))
        {
            if (!result.ContainsKey(i) && used.Add(s))
            {
                result[i] = s;
            }
        }

        return result;
    }

    // The nearest person of the same face version: in the catalog (by its index), or created for this asset just now.
    private async Task<Person?> NearestAsync(
        float[] embedding, int version, List<Person> created, List<Face> kept, CancellationToken cancellationToken)
    {
        var vector = new Vector(embedding);
        var stored = await db.People
            .Where(p => p.Centroid != null && p.FacesVersion == version)
            .OrderBy(p => p.Centroid!.CosineDistance(vector))
            .Select(p => new { p.Id, Centroid = p.Centroid! })
            .FirstOrDefaultAsync(cancellationToken);

        var candidates = new List<(float[] Centroid, Func<Task<Person?>> Person)>();
        if (stored is not null)
        {
            candidates.Add((stored.Centroid.ToArray(), async () => await db.People.FindAsync([stored.Id], cancellationToken)));
        }

        foreach (var person in created)
        {
            if (FaceMath.Centroid(kept.Where(f => ReferenceEquals(f.Person, person)).Select(f => f.Embedding.ToArray())) is { } centroid)
            {
                candidates.Add((centroid, () => Task.FromResult<Person?>(person)));
            }
        }

        return Choose([.. candidates.Select(c => c.Centroid)], embedding, _threshold) is { } best
            ? await candidates[best].Person()
            : null;
    }

    /// <summary>
    /// The person's face count (every face not excluded, of any version) and centroid (the faces of the newest version
    /// it has, only). An unnamed person left with no face is removed; a named one is kept for the family to see.
    /// </summary>
    private async Task RecomputeAsync(Person person, string assetId, int version, List<Face> kept, CancellationToken cancellationToken)
    {
        var faces = new List<(float[] Embedding, int Version)>();
        if (person.Id != 0)
        {
            var id = person.Id;
            var elsewhere = await db.Faces.AsNoTracking()
                .Where(f => f.PersonId == id && f.AssetId != assetId && f.Assignment != FaceAssignments.Excluded)
                .Join(db.Assets, f => f.AssetId, a => a.Id, (f, a) => new { f.Embedding, Version = a.FacesVersion ?? 1 })
                .ToListAsync(cancellationToken);
            faces.AddRange(elsewhere.Select(f => (f.Embedding.ToArray(), f.Version)));
        }

        faces.AddRange(kept
            .Where(f => f.Assignment != FaceAssignments.Excluded
                && (ReferenceEquals(f.Person, person) || (person.Id != 0 && f.PersonId == person.Id)))
            .Select(f => (f.Embedding.ToArray(), version)));

        if (faces.Count == 0)
        {
            if (person.Name is null && person.UserEmail is null)
            {
                db.People.Remove(person);
                return;
            }

            person.FaceCount = 0;
            person.Centroid = null;
            return;
        }

        var newest = faces.Max(f => f.Version);
        person.FaceCount = faces.Count;
        person.FacesVersion = newest;
        person.Centroid = FaceMath.Centroid(faces.Where(f => f.Version == newest).Select(f => f.Embedding)) is { } centroid
            ? new Vector(centroid)
            : null;
    }
}
