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
/// asset's earlier ones, keeping the family's curation of each face found again, gives every new face the most similar
/// person of its face version when similar enough (at most one face of an asset per person) or a new person otherwise,
/// and recomputes the centroid and face count of every person it touched. It only changes the tracked context: the
/// caller saves, inside the transaction that holds <see cref="LockAsync"/>, so the asset, its faces and its people
/// commit together and no other import changes people meanwhile.
/// </summary>
public sealed class FaceAssigner(CatalogDbContext db, FaceAssignerOptions? options = null)
{
    /// <summary>A stored face is the same face as an incoming one when their boxes overlap this much (D-047).</summary>
    public const double SameBoxIoU = 0.5;

    /// <summary>The advisory lock every import takes before it assigns faces: one fixed key for all of people.</summary>
    public const long PeopleLockKey = 0x5E91AB19;

    private readonly double _threshold = (options ?? new FaceAssignerOptions()).Threshold;

    /// <summary>
    /// Waits for, and takes until the end of the current transaction, the lock that serializes face assignment, so two
    /// imports at once (the queue job and <c>import-all</c>) never lose a count or a centroid or make the same person
    /// twice. The caller must have begun a transaction.
    /// </summary>
    public static async Task LockAsync(CatalogDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Face assignment is locked inside a transaction, which holds the lock until it ends.");
        }

        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({PeopleLockKey})", cancellationToken);
    }

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
        var touched = new List<Person>();
        foreach (var id in stored.Where(f => f.PersonId is not null).Select(f => f.PersonId!.Value).Distinct())
        {
            if (await db.People.FindAsync([id], cancellationToken) is { } person)
            {
                touched.Add(person);
            }
        }

        var kept = new List<Face>();
        var unmatched = new List<IncomingFace>();
        var matches = Match(stored.Select(f => f.Box).ToList(), incoming.Select(f => f.Box).ToList());
        for (var i = 0; i < incoming.Count; i++)
        {
            if (matches.TryGetValue(i, out var storedIndex))
            {
                // The same face found again: new numbers, the same person and assignment.
                var row = stored[storedIndex];
                row.Box = incoming[i].Box;
                row.Score = incoming[i].Score;
                row.Embedding = new Vector(incoming[i].Embedding);
                kept.Add(row);
            }
            else
            {
                unmatched.Add(incoming[i]);
            }
        }

        var matched = matches.Values.ToHashSet();
        for (var i = 0; i < stored.Count; i++)
        {
            if (!matched.Contains(i))
            {
                db.Faces.Remove(stored[i]);
            }
        }

        // People this asset's kept faces already belong to: one face of an asset per person.
        // A face excluded from a person, or not a face, does not stop a real face of this photo from joining them.
        var used = kept.Where(f => f.PersonId is not null && FaceAssignments.Counts(f.Assignment)).Select(f => f.PersonId!.Value).ToHashSet();
        var chosen = await ChooseExistingAsync(unmatched, version, used, cancellationToken);
        for (var i = 0; i < unmatched.Count; i++)
        {
            var face = unmatched[i];
            Person person;
            if (chosen.TryGetValue(i, out var id))
            {
                person = await db.People.FindAsync([id], cancellationToken)
                    ?? throw new InvalidOperationException($"Person {id} disappeared while the people were locked.");
            }
            else
            {
                person = new Person { FacesVersion = version };
                db.People.Add(person);
            }

            if (!touched.Contains(person))
            {
                touched.Add(person);
            }

            var added = new Face
            {
                AssetId = assetId,
                Box = face.Box,
                Score = face.Score,
                Embedding = new Vector(face.Embedding),
                Person = person,
                Assignment = FaceAssignments.Auto,
            };
            db.Faces.Add(added);
            kept.Add(added);
        }

        foreach (var person in touched)
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
        var pairs = Assign([embedding], centroids, threshold);
        return pairs.TryGetValue(0, out var best) ? best : null;
    }

    /// <summary>
    /// Faces to people (face index to centroid index), greedily by similarity: the most similar pair first, each face
    /// and each person once, and only pairs whose cosine similarity reaches <paramref name="threshold"/>. Faces left
    /// out start new people, so two faces of one photo never become one person.
    /// </summary>
    public static Dictionary<int, int> Assign(IReadOnlyList<float[]> faces, IReadOnlyList<float[]> centroids, double threshold)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(centroids);
        var pairs = new List<(int Face, int Person, double Similarity)>();
        for (var f = 0; f < faces.Count; f++)
        {
            for (var p = 0; p < centroids.Count; p++)
            {
                var similarity = FaceMath.Cosine(centroids[p], faces[f]);
                if (similarity >= threshold)
                {
                    pairs.Add((f, p, similarity));
                }
            }
        }

        var result = new Dictionary<int, int>();
        var taken = new HashSet<int>();
        foreach (var (f, p, _) in pairs.OrderByDescending(x => x.Similarity).ThenBy(x => x.Face).ThenBy(x => x.Person))
        {
            if (!result.ContainsKey(f) && taken.Add(p))
            {
                result[f] = p;
            }
        }

        return result;
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

    /// <summary>
    /// For each new face, the stored person it joins (by id), if any: each face's nearest people of the same face
    /// version not already holding a face of this asset, ranked exactly (the people table is small, and the HNSW
    /// index could skip the nearest one when the version filter applies after it, D-070), then <see cref="Assign"/>.
    /// </summary>
    private async Task<Dictionary<int, long>> ChooseExistingAsync(
        List<IncomingFace> faces, int version, HashSet<long> used, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<long, float[]>();
        foreach (var face in faces)
        {
            var vector = new Vector(face.Embedding);
            // Adding zero keeps the order and rules the index out, so every person is compared.
            var nearest = await db.People.AsNoTracking()
                .Where(p => p.Centroid != null && p.FacesVersion == version && !used.Contains(p.Id))
                .OrderBy(p => p.Centroid!.CosineDistance(vector) + 0.0).ThenBy(p => p.Id)
                .Take(faces.Count)
                .Select(p => new { p.Id, Centroid = p.Centroid! })
                .ToListAsync(cancellationToken);
            foreach (var person in nearest)
            {
                candidates.TryAdd(person.Id, person.Centroid.ToArray());
            }
        }

        var ids = candidates.Keys.ToList();
        return Assign([.. faces.Select(f => f.Embedding)], [.. ids.Select(id => candidates[id])], _threshold)
            .ToDictionary(pair => pair.Key, pair => ids[pair.Value]);
    }

    /// <summary>
    /// The person's count and centroid recomputed (as below) from the catalog as saved, for the family's edits (S-020):
    /// called after their SaveChanges, inside the transaction that holds <see cref="LockAsync"/>, so imports and edits
    /// never count over each other. An unnamed person no face points to any more is removed.
    /// </summary>
    public Task RecomputeAsync(Person person, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(person);
        // No asset is being replaced: every face is read as saved (asset ids are never empty).
        return RecomputeAsync(person, string.Empty, 0, [], cancellationToken);
    }

    /// <summary>
    /// The person's face count (every face not excluded, of any version) and centroid (the normalized mean of the faces
    /// of the newest version it has, only), from the catalog's other assets, aggregated in the database, and this
    /// asset's faces in memory. An unnamed person no face points to any more is removed; one with only excluded faces
    /// left is kept (count 0, no centroid), so the family's exclusion stays, and so is a named one.
    /// </summary>
    private async Task RecomputeAsync(Person person, string assetId, int version, List<Face> kept, CancellationToken cancellationToken)
    {
        var groups = new List<FaceGroup>();
        if (person.Id != 0)
        {
            groups = await db.Database.SqlQuery<FaceGroup>($"""
                SELECT coalesce(a.faces_version, 1) AS "Version",
                       count(*) FILTER (WHERE f.assignment IN ('auto', 'manual'))::int AS "Counted",
                       count(*)::int AS "Rows",
                       avg(f.embedding) FILTER (WHERE f.assignment IN ('auto', 'manual')) AS "Mean"
                FROM faces f JOIN assets a ON a.id = f.asset_id
                WHERE f.person_id = {person.Id} AND f.asset_id <> {assetId}
                GROUP BY 1
                """).ToListAsync(cancellationToken);
        }

        var mine = kept.Where(f => ReferenceEquals(f.Person, person) || (person.Id != 0 && f.PersonId == person.Id)).ToList();
        var counted = mine.Where(f => FaceAssignments.Counts(f.Assignment)).ToList();
        if (groups.Sum(g => g.Rows) + mine.Count == 0 && person.Name is null && person.UserEmail is null)
        {
            db.People.Remove(person);
            return;
        }

        person.FaceCount = groups.Sum(g => g.Counted) + counted.Count;
        var versions = groups.Where(g => g.Counted > 0).Select(g => g.Version).ToList();
        if (counted.Count > 0)
        {
            versions.Add(version);
        }

        if (versions.Count == 0)
        {
            person.Centroid = null;
            return;
        }

        var newest = versions.Max();
        double[]? sum = null;
        if (groups.SingleOrDefault(g => g.Version == newest && g.Counted > 0) is { Mean: { } mean } group)
        {
            sum = [.. mean.ToArray().Select(v => (double)v * group.Counted)];
        }

        if (newest == version)
        {
            foreach (var face in counted)
            {
                var values = face.Embedding.ToArray();
                sum ??= new double[values.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    sum[i] += values[i];
                }
            }
        }

        person.FacesVersion = newest;
        person.Centroid = sum is not null && FaceMath.Centroid([[.. sum.Select(v => (float)v)]]) is { } centroid
            ? new Vector(centroid)
            : null;
    }

    private sealed record FaceGroup(int Version, int Counted, int Rows, Vector? Mean);
}
