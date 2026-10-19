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
        var description = incoming.Derivatives.Count == 0 && existing.Derivatives.Count > 0 ? existing : incoming;

        return description with
        {
            Sources = MergeSources(existing.Sources, incoming.Sources),
            Derivatives = MergeDerivatives(existing.Derivatives, incoming.Derivatives),
        };
    }

    private static List<ManifestSource> MergeSources(IReadOnlyList<ManifestSource> existing, IReadOnlyList<ManifestSource> incoming)
    {
        var merged = existing.ToList();
        foreach (var source in incoming)
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

    private static List<ManifestDerivative> MergeDerivatives(
        IReadOnlyList<ManifestDerivative> existing, IReadOnlyList<ManifestDerivative> incoming)
    {
        var byName = existing.ToDictionary(d => d.Name, StringComparer.Ordinal);
        foreach (var derivative in incoming)
        {
            byName[derivative.Name] = derivative;
        }

        return [.. byName.Values.OrderBy(d => d.Name, StringComparer.Ordinal)];
    }
}
