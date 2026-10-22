using System.Numerics;

namespace Sepiabox.Catalog.Organization;

/// <summary>A photo as the burst finder reads it: when it was taken, its hash and how sharp it is (S-027).</summary>
public sealed record BurstPhoto(string Id, DateTime CapturedAt, string DHash, double Sharpness);

/// <summary>
/// Finds bursts (S-027, D-098): runs of photos, each taken at most <see cref="MaxGapSeconds"/> after the one before and
/// looking like it (their 64-bit difference hashes at most <see cref="MaxDistance"/> bits apart). Pure and deterministic.
/// </summary>
public static class BurstFinder
{
    public const double MaxGapSeconds = 2;
    public const int MaxDistance = 10;

    /// <summary>
    /// The bursts of two photos or more, each in capture order (then by id), the oldest burst first. A photo follows the one
    /// before it in the burst: a slow change across a long run still chains, which is what a burst of a moving subject is.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<BurstPhoto>> Find(IEnumerable<BurstPhoto> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);
        var bursts = new List<IReadOnlyList<BurstPhoto>>();
        var current = new List<BurstPhoto>();
        foreach (var photo in photos.OrderBy(p => p.CapturedAt).ThenBy(p => p.Id, StringComparer.Ordinal))
        {
            if (current.Count > 0 && !Follows(current[^1], photo))
            {
                Close();
            }

            current.Add(photo);
        }

        Close();
        return bursts;

        void Close()
        {
            if (current.Count > 1)
            {
                bursts.Add(current);
            }

            current = [];
        }
    }

    /// <summary>The photo to keep of a burst: the sharpest, the earliest when equally sharp.</summary>
    public static BurstPhoto Sharpest(IReadOnlyList<BurstPhoto> burst)
    {
        ArgumentNullException.ThrowIfNull(burst);
        return burst.OrderByDescending(p => p.Sharpness).ThenBy(p => p.CapturedAt).ThenBy(p => p.Id, StringComparer.Ordinal).First();
    }

    private static bool Follows(BurstPhoto previous, BurstPhoto next) =>
        (next.CapturedAt - previous.CapturedAt).TotalSeconds <= MaxGapSeconds && Distance(previous.DHash, next.DHash) <= MaxDistance;

    // Hashes are 16 hex characters (the importer stores only those); anything else is far from everything.
    private static int Distance(string a, string b) =>
        ulong.TryParse(a, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var x)
        && ulong.TryParse(b, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var y)
            ? BitOperations.PopCount(x ^ y)
            : int.MaxValue;
}
