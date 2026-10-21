using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Manifests;

/// <summary>
/// Combines two manifests of the same asset written by different work folders (D-036), so uploading a
/// second drive never loses what the first one described.
/// </summary>
public static class ManifestMerge
{
    public static Manifest Merge(Manifest existing, Manifest incoming)
    {
        if (existing.Id != incoming.Id)
        {
            throw new ArgumentException($"Cannot merge manifest {incoming.Id} into {existing.Id}.", nameof(incoming));
        }

        // A manifest without derivatives comes from a run that could not finish the file: a video
        // waiting for FFmpeg (D-029) or a photo that did not decode (D-028). Its description is weaker.
        var (description, other) = incoming.Derivatives.Count == 0 && existing.Derivatives.Count > 0
            ? (existing, incoming)
            : (incoming, existing);

        // The newer erase wins (D-081), and erases the faces of either side found at or before it, before the sides'
        // faces are compared: faces found after it, on any computer, win over it whichever uploads last.
        var erasedAt = Later(existing.FacesErasedAt, incoming.FacesErasedAt);
        var (existingFaces, incomingFaces) = (Surviving(existing, erasedAt), Surviving(incoming, erasedAt));
        var faces = FacesOf(existingFaces, incomingFaces);

        return description with
        {
            // Ranked by source, so the date does not depend on which drive uploads first (D-046).
            Captured = CaptureDate.Stronger(description.Captured, other.Captured),
            // A manifest written before video locations were read (D-060) has none; it must not erase one found since.
            Location = description.Location ?? other.Location,
            Sources = MergeSources([.. existing.Sources, .. incoming.Sources]),
            Derivatives = MergeDerivatives([.. existing.Derivatives, .. incoming.Derivatives]),
            Embeddings = MergeEmbeddings([.. existing.Embeddings, .. incoming.Embeddings]),
            // Stored once any side stores it (S-063, D-088), whichever side is the description.
            OriginalStored = MergeOriginalStored(existing, incoming),
            FacesProcessed = faces.FacesProcessed,
            Faces = faces.Faces,
            FacesVersion = FacesVersionOf(faces, existingFaces, incomingFaces),
            FacesDetectedAt = FacesDetectedAtOf(faces, existingFaces, incomingFaces),
            FacesErasedAt = erasedAt,
        };
    }

    /// <summary>
    /// How far after its blob was written a manifest's <c>facesDetectedAt</c> may be (D-081): beyond it, the computer's
    /// clock was ahead when it ran <c>process</c>, and the time is not trusted. More than <c>upload</c>'s 5-minute clock
    /// check plus a second of rounding, so faces found by a clock it accepts are always trusted.
    /// </summary>
    public static readonly TimeSpan DetectionTolerance = TimeSpan.FromMinutes(6);

    /// <summary>The manifest as if it had not looked for faces: no faces, version or detection time.</summary>
    public static Manifest WithoutFaces(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest with { FacesProcessed = false, Faces = [], FacesVersion = null, FacesDetectedAt = null };
    }

    /// <summary>Whether <paramref name="manifest"/>'s detection time is plausible for a blob written at <paramref name="writtenAt"/>.</summary>
    public static bool DetectionTrusted(Manifest manifest, DateTimeOffset? writtenAt)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.FacesDetectedAt is not { } detected || writtenAt is not { } written || detected <= written + DetectionTolerance;
    }

    /// <summary>
    /// The erase writer (S-022, D-081): <paramref name="stored"/> with the tombstone <paramref name="erasedAt"/> merged
    /// in, so its faces found at or before it are gone and the newer of the two tombstones is kept. Everything else is
    /// the stored manifest's own, so an erase never changes what the asset is. Faces said to be found more than
    /// <see cref="DetectionTolerance"/> after the blob was written (<paramref name="writtenAt"/>) are erased too.
    /// </summary>
    public static Manifest EraseFaces(Manifest stored, DateTimeOffset erasedAt, DateTimeOffset? writtenAt = null)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (!DetectionTrusted(stored, writtenAt))
        {
            // Found "in the future": a clock that was ahead, so these faces may be older than the erase.
            stored = WithoutFaces(stored);
        }

        return Merge(stored, stored with
        {
            FacesProcessed = false,
            Faces = [],
            FacesVersion = null,
            FacesDetectedAt = null,
            FacesErasedAt = erasedAt.ToUniversalTime(),
        });
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);

    // A side whose faces the tombstone erases counts as one that never looked for faces.
    private static Manifest Surviving(Manifest side, DateTimeOffset? erasedAt) =>
        !side.FacesProcessed || side.FacesSurvive(erasedAt)
            ? side
            : side with { FacesProcessed = false, Faces = [], FacesVersion = null, FacesDetectedAt = null };

    // The detection time of the chosen faces, the same in either order: the latest of the sides that found those faces.
    private static DateTimeOffset? FacesDetectedAtOf(Manifest chosen, Manifest existing, Manifest incoming)
    {
        if (!chosen.FacesProcessed)
        {
            return null;
        }

        return new[] { existing, incoming }
            .Where(side => side.FacesProcessed && side.EffectiveFacesVersion == chosen.EffectiveFacesVersion && side.Faces.SequenceEqual(chosen.Faces))
            .Select(side => side.FacesDetectedAt)
            .Aggregate((DateTimeOffset?)null, Later);
    }

    /// <summary>
    /// The side whose faces the merge keeps (S-018, D-047). Faces are opt-in: a work folder processed without
    /// --faces knows nothing about them, so it never replaces faces another one found. When both sides looked, the
    /// choice depends only on the faces, never on which side is incoming, so uploads of the same asset from two
    /// computers converge on one blob instead of flipping it: the newer face pipeline wins, then more faces, then
    /// the larger list in <see cref="ManifestFace.Order"/>. That is a maximum over a total order, so merges
    /// in any order and grouping give the same faces.
    /// </summary>
    private static Manifest FacesOf(Manifest existing, Manifest incoming)
    {
        if (existing.FacesProcessed != incoming.FacesProcessed)
        {
            return existing.FacesProcessed ? existing : incoming;
        }

        if (!existing.FacesProcessed)
        {
            return incoming;
        }

        var order = existing.EffectiveFacesVersion.CompareTo(incoming.EffectiveFacesVersion);
        order = order != 0 ? order : existing.Faces.Count.CompareTo(incoming.Faces.Count);
        // Only a shortcut: equal lists also compare as 0 below, since the order gives 0 exactly for equal faces.
        if (order == 0 && !existing.Faces.SequenceEqual(incoming.Faces))
        {
            for (var i = 0; order == 0 && i < existing.Faces.Count; i++)
            {
                order = ManifestFace.Order.Compare(existing.Faces[i], incoming.Faces[i]);
            }
        }

        return order > 0 ? existing : incoming;
    }

    /// <summary>
    /// The stored <c>facesVersion</c> of the merge, the same in either order: none without faces processed; otherwise
    /// the chosen version, written out whenever either side that found those same faces wrote it. A tie between faces
    /// written before the field existed (read as 1) and the same faces at version 1 then always stores 1.
    /// </summary>
    private static int? FacesVersionOf(Manifest chosen, Manifest existing, Manifest incoming)
    {
        if (!chosen.FacesProcessed)
        {
            return null;
        }

        var written = new[] { existing, incoming }.Any(side =>
            side.FacesProcessed && side.FacesVersion is not null
            && side.EffectiveFacesVersion == chosen.EffectiveFacesVersion && side.Faces.SequenceEqual(chosen.Faces));
        return written ? chosen.EffectiveFacesVersion : null;
    }

    /// <summary>
    /// The form a manifest is stored in: sources and derivatives deduplicated and sorted, paths with '/'.
    /// Writing it lets the next upload of the same manifest see that nothing changed.
    /// </summary>
    public static Manifest Canonical(Manifest manifest) => manifest with
    {
        Sources = MergeSources(manifest.Sources),
        Derivatives = MergeDerivatives(manifest.Derivatives),
        Embeddings = MergeEmbeddings(manifest.Embeddings),
        OriginalStored = manifest.IsOriginalStored ? null : false,
    };

    // False only when both sides say so; written as absent when true, so equal manifests serialize equally.
    private static bool? MergeOriginalStored(Manifest a, Manifest b) => a.IsOriginalStored || b.IsOriginalStored ? null : false;

    // Later entries win for the same file, unless they are older. The url merges like the other fields (the newer
    // modifiedAt wins) and a present one beats an absent one (S-063, D-091); on a tie the larger url wins, so the result
    // does not depend on the order.
    private static List<ManifestSource> MergeSources(IEnumerable<ManifestSource> sources)
    {
        var merged = new List<ManifestSource>();
        foreach (var source in sources.Select(s => s with { Path = ManifestSource.NormalizePath(s.Path) }))
        {
            var index = merged.FindIndex(s => s.IsSameFile(source));
            if (index < 0)
            {
                merged.Add(source);
                continue;
            }

            var kept = merged[index];
            var (newer, older) = source.ModifiedAt > kept.ModifiedAt ? (source, kept) : (kept, source);
            merged[index] = newer with
            {
                Url = source.ModifiedAt == kept.ModifiedAt ? LargerUrl(kept.Url, source.Url) : newer.Url ?? older.Url,
            };
        }

        return [.. merged
            .OrderBy(s => s.Type, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => s.Path, StringComparer.Ordinal)];
    }

    private static string? LargerUrl(string? a, string? b) =>
        a is null ? b : b is null ? a : string.CompareOrdinal(a, b) >= 0 ? a : b;

    // Later entries win for the same name.
    private static List<ManifestDerivative> MergeDerivatives(IEnumerable<ManifestDerivative> derivatives)
    {
        var byName = new Dictionary<string, ManifestDerivative>(StringComparer.Ordinal);
        foreach (var derivative in derivatives)
        {
            byName[derivative.Name] = derivative;
        }

        return [.. byName.Values.OrderBy(d => d.Name, StringComparer.Ordinal)];
    }

    // By model key, later entries winning (D-036): a drive processed without a model, or with another model
    // version, never drops the vectors stored for other keys. Sorted so equal manifests serialize equally.
    private static SortedDictionary<string, JsonNode> MergeEmbeddings(IEnumerable<KeyValuePair<string, JsonNode>> embeddings)
    {
        var byModel = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var (model, value) in embeddings)
        {
            byModel[model] = value.DeepClone();
        }

        return byModel;
    }
}
