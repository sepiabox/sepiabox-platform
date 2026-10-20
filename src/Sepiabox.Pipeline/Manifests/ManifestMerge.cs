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

        // Faces are opt-in (S-018): a work folder processed without --faces knows nothing about them, so it never
        // replaces faces that another one found.
        var faces = incoming.FacesProcessed || !existing.FacesProcessed ? incoming : existing;

        return description with
        {
            // Ranked by source, so the date does not depend on which drive uploads first (D-046).
            Captured = CaptureDate.Stronger(description.Captured, other.Captured),
            Sources = MergeSources([.. existing.Sources, .. incoming.Sources]),
            Derivatives = MergeDerivatives([.. existing.Derivatives, .. incoming.Derivatives]),
            Embeddings = MergeEmbeddings([.. existing.Embeddings, .. incoming.Embeddings]),
            FacesProcessed = faces.FacesProcessed,
            Faces = faces.Faces,
        };
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
