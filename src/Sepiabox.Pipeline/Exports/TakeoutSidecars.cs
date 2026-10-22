using System.Text.RegularExpressions;

namespace Sepiabox.Pipeline.Exports;

/// <summary>
/// Finds the JSON sidecar of each media file in one folder of a Google Takeout export (S-046, D-106), over names only.
/// Takeout's names have changed over the years and cut long ones, so this is the one place that knows the variants:
/// <c>IMG.jpg.json</c>; <c>IMG.jpg.supplemental-metadata.json</c> in newer exports; sidecar names cut at a length limit;
/// the second copy <c>IMG(1).jpg</c> with the sidecar <c>IMG.jpg(1).json</c>; and an edited copy <c>IMG-edited.jpg</c>
/// (the word is in the export's language) that shares the original's sidecar.
/// </summary>
public static partial class TakeoutSidecars
{
    /// <summary>What newer exports put between the media's name and <c>.json</c>.</summary>
    public const string Supplemental = ".supplemental-metadata";

    /// <summary>
    /// The shortest cut sidecar name (without <c>.json</c>) that counts as a cut name: Takeout limits a whole sidecar file
    /// name to 51 characters, so a cut one has 46 before <c>.json</c>; a little less is accepted, in case the limit varies.
    /// To be confirmed against a real export (D-106).
    /// </summary>
    public const int MinimumCutLength = 40;

    // The word Google adds to an edited copy, by language of the account: "IMG_0001-edited.jpg", "IMG_0001-bearbeitet.jpg".
    private static readonly string[] s_editedWords =
    [
        "edited", "bearbeitet", "modifié", "modifie", "editado", "modificato", "bewerkt", "redigerad", "redigeret", "muokattu",
        "edytowane", "upraveno", "szerkesztett", "düzenlendi", "отредактировано", "編集済み", "편집됨",
    ];

    [GeneratedRegex(@"^(?<base>.*)\((?<n>\d+)\)(?<ext>\.[^.]*)$")]
    private static partial Regex Duplicate();

    /// <summary>
    /// For each of <paramref name="mediaNames"/> (file names in one folder), the file name of its sidecar among
    /// <paramref name="jsonNames"/> (the <c>.json</c> files of the same folder), or null. A sidecar may serve several
    /// media files (an edited copy shares the original's). A cut sidecar name is matched only when it points at one
    /// media file and no other media file's own sidecar is it.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Match(IReadOnlyList<string> mediaNames, IReadOnlyList<string> jsonNames)
    {
        ArgumentNullException.ThrowIfNull(mediaNames);
        ArgumentNullException.ThrowIfNull(jsonNames);

        var sidecars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in jsonNames.Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            sidecars.TryAdd(json[..^".json".Length], json);
        }

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var media in mediaNames)
        {
            var found = Stems(media).Select(stem => sidecars.TryGetValue(stem, out var json) ? json : null).FirstOrDefault(json => json is not null);
            result[media] = found;
            if (found is not null)
            {
                claimed.Add(found);
            }
        }

        foreach (var media in mediaNames.Where(m => result[m] is null))
        {
            var stems = Stems(media);
            var cut = sidecars
                .Where(pair => !claimed.Contains(pair.Value) && pair.Key.Length >= MinimumCutLength
                    && stems.Any(stem => stem.Length > pair.Key.Length && stem.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => pair.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Two sidecars, or a sidecar that two media files could own, is a guess; no sidecar is the honest answer.
            if (cut.Count == 1 && !mediaNames.Any(other => other != media && Stems(other).Any(s => s.StartsWith(cut[0][..^".json".Length], StringComparison.OrdinalIgnoreCase))))
            {
                result[media] = cut[0];
            }
        }

        return result;
    }

    /// <summary>
    /// The sidecar names (without <c>.json</c>) that <paramref name="media"/> may have, most specific first: its own, and
    /// those of the original it is an edited copy of.
    /// </summary>
    public static IReadOnlyList<string> Stems(string media)
    {
        var names = new List<string> { media };
        var original = Original(media);
        if (original != media)
        {
            names.Add(original);
        }

        var stems = new List<string>();
        foreach (var name in names)
        {
            // The second copy of a name: "IMG(1).jpg" has the sidecar "IMG.jpg(1).json", never the first copy's.
            if (Duplicate().Match(name) is { Success: true } duplicate)
            {
                var baseName = duplicate.Groups["base"].Value + duplicate.Groups["ext"].Value;
                var n = duplicate.Groups["n"].Value;
                stems.Add($"{baseName}({n})");
                stems.Add($"{baseName}{Supplemental}({n})");
            }

            stems.Add(name);
            stems.Add(name + Supplemental);
        }

        return [.. stems.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The name of the file an edited copy was made from, or <paramref name="media"/> itself.</summary>
    public static string Original(string media)
    {
        var extension = Path.GetExtension(media);
        var stem = media[..^extension.Length];
        foreach (var word in s_editedWords)
        {
            var suffix = "-" + word;
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && stem.Length > suffix.Length)
            {
                return stem[..^suffix.Length] + extension;
            }
        }

        return media;
    }
}
