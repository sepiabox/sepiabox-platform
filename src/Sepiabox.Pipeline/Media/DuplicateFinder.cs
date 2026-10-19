using System.Security.Cryptography;

namespace Sepiabox.Pipeline.Media;

public sealed record FileSample(string Path, long Size);

public sealed record DuplicateGroup(long Size, IReadOnlyList<string> Paths)
{
    /// <summary>Bytes freed by keeping a single copy.</summary>
    public long RedundantBytes => Size * (Paths.Count - 1);
}

public sealed record DuplicateReport(IReadOnlyList<DuplicateGroup> Groups, IReadOnlyList<string> Unreadable);

/// <summary>
/// Finds byte-identical files. Most files are ruled out by size alone; files of the same size are
/// compared by a hash of their first and last blocks, and only those still tied are read in full.
/// </summary>
public static class DuplicateFinder
{
    private const int EdgeBlockSize = 64 * 1024;

    public static DuplicateReport Find(IEnumerable<FileSample> files, CancellationToken cancellationToken = default)
    {
        var groups = new List<DuplicateGroup>();
        var unreadable = new List<string>();

        var sameSizeGroups = files
            .Where(file => file.Size > 0)
            .GroupBy(file => file.Size)
            .Where(group => group.Count() > 1);

        foreach (var sameSize in sameSizeGroups)
        {
            var size = sameSize.Key;
            List<List<FileSample>> candidates = [sameSize.ToList()];

            if (size > 2 * EdgeBlockSize)
            {
                candidates = SplitByHash(candidates, EdgeHash, unreadable, cancellationToken);
            }

            foreach (var group in SplitByHash(candidates, FullHash, unreadable, cancellationToken))
            {
                var paths = group.Select(file => file.Path).Order(StringComparer.Ordinal).ToList();
                groups.Add(new DuplicateGroup(size, paths));
            }
        }

        return new DuplicateReport(groups, unreadable);
    }

    private static List<List<FileSample>> SplitByHash(
        List<List<FileSample>> candidates,
        Func<string, string> hash,
        List<string> unreadable,
        CancellationToken cancellationToken)
    {
        var result = new List<List<FileSample>>();
        foreach (var candidate in candidates)
        {
            var byHash = new Dictionary<string, List<FileSample>>(StringComparer.Ordinal);
            foreach (var file in candidate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key;
                try
                {
                    key = hash(file.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    unreadable.Add(file.Path);
                    continue;
                }

                if (!byHash.TryGetValue(key, out var sameHash))
                {
                    byHash[key] = sameHash = [];
                }

                sameHash.Add(file);
            }

            result.AddRange(byHash.Values.Where(sameHash => sameHash.Count > 1));
        }

        return result;
    }

    private static string EdgeHash(string path)
    {
        using var stream = MediaFile.OpenRead(path);
        var buffer = new byte[2 * EdgeBlockSize];
        stream.ReadExactly(buffer, 0, EdgeBlockSize);
        stream.Seek(-EdgeBlockSize, SeekOrigin.End);
        stream.ReadExactly(buffer, EdgeBlockSize, EdgeBlockSize);
        return Convert.ToHexString(SHA256.HashData(buffer));
    }

    private static string FullHash(string path) => MediaFile.Sha256Hex(path);
}
