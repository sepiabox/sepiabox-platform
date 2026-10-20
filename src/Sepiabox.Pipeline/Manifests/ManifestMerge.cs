using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Manifests;

/// <summary>
/// Combines two manifests of the same asset written by different work folders (D-036), so uploading a
/// second drive never loses what the first one described.
/// </summary>
public static class ManifestMerge
{
    // Capture dates rank no date < a date from the path < a date from metadata.
    private const int MetadataRank = 2;

    public static Manifest Merge(Manifest existing, Manifest incoming)
    {
        if (existing.Id != incoming.Id)
        {
            throw new ArgumentException($"Cannot merge manifest {incoming.Id} into {existing.Id}.", nameof(incoming));
        }

        // A manifest without derivatives comes from a run that could not finish the file: a video
        // waiting for FFmpeg (D-029) or a photo that did not decode (D-028). Its description is weaker.
        var description = incoming.Derivatives.Count == 0 && existing.Derivatives.Count > 0 ? existing : incoming;
        var dated = MergeCaptureDate(existing, incoming, description);

        return description with
        {
            CapturedAt = dated.CapturedAt,
            CapturedAtSource = dated.CapturedAtSource,
            Sources = MergeSources([.. existing.Sources, .. incoming.Sources]),
            Derivatives = MergeDerivatives([.. existing.Derivatives, .. incoming.Derivatives]),
            Embeddings = MergeEmbeddings([.. existing.Embeddings, .. incoming.Embeddings]),
        };
    }

    /// <summary>
    /// The manifest whose capture date to keep (D-046). The same bytes have the same metadata, but each copy has its
    /// own path, so path dates can disagree. A metadata date is never replaced by a path date and a date is never
    /// replaced by no date; between path dates the incoming one wins, and between metadata dates
    /// <paramref name="description"/>'s, as for the rest of the description. A date without
    /// <see cref="Manifest.CapturedAtSource"/> is from a manifest written before S-043, when every date came from
    /// metadata.
    /// </summary>
    public static Manifest MergeCaptureDate(Manifest existing, Manifest incoming, Manifest description)
    {
        var (existingRank, incomingRank) = (DateRank(existing), DateRank(incoming));
        if (existingRank != incomingRank)
        {
            return existingRank > incomingRank ? existing : incoming;
        }

        return existingRank == MetadataRank ? description : incoming;
    }

    private static int DateRank(Manifest manifest) => manifest switch
    {
        { CapturedAt: null } => 0,
        { CapturedAtSource: null or CaptureDateSource.Metadata } => MetadataRank,
        _ => 1,
    };

    /// <summary>
    /// The form a manifest is stored in: sources and derivatives deduplicated and sorted, paths with '/'.
    /// Writing it lets the next upload of the same manifest see that nothing changed.
    /// </summary>
    public static Manifest Canonical(Manifest manifest) => manifest with
    {
        Sources = MergeSources(manifest.Sources),
        Derivatives = MergeDerivatives(manifest.Derivatives),
        Embeddings = MergeEmbeddings(manifest.Embeddings),
    };

    // Later entries win for the same file, unless they are older.
    private static List<ManifestSource> MergeSources(IEnumerable<ManifestSource> sources)
    {
        var merged = new List<ManifestSource>();
        foreach (var source in sources.Select(s => s with { Path = ManifestSource.NormalizePath(s.Path) }))
        {
            var index = merged.FindIndex(s => s.IsSameFile(source));
            if (index < 0)
            {
                merged.Add(source);
            }
            else if (source.ModifiedAt > merged[index].ModifiedAt)
            {
                merged[index] = source;
            }
        }

        return [.. merged
            .OrderBy(s => s.Type, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => s.Path, StringComparer.Ordinal)];
    }

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
