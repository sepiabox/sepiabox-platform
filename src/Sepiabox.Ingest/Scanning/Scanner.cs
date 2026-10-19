using System.Diagnostics;
using System.Globalization;
using System.IO.Enumeration;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Scanning;

/// <summary>
/// Reads a folder tree without changing anything in it and reports what it holds.
/// Files are read one at a time, which is what a spinning external drive handles best.
/// </summary>
public static class Scanner
{
    // Operating system and NAS folders that never hold the user's own photos.
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN", "System Volume Information", ".Trashes", ".Spotlight-V100", ".fseventsd",
        ".DocumentRevisions-V100", ".TemporaryItems", "@eaDir", ".thumbnails",
    };

    public static ScanResult Scan(ScanOptions options, IProgress<int>? filesSeen = null, CancellationToken cancellationToken = default)
    {
        // On Windows "E:" means the current folder on drive E, not the drive itself.
        var requested = options.Root is [_, ':'] ? options.Root + Path.DirectorySeparatorChar : options.Root;
        var root = Path.GetFullPath(requested);
        if (!System.IO.Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Folder not found: {root}");
        }

        var stopwatch = Stopwatch.StartNew();
        var totals = Enum.GetValues<MediaKind>().ToDictionary(kind => kind, _ => new KindTotals(0, 0));
        var mediaByYear = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var filesByExtension = new Dictionary<string, int>(StringComparer.Ordinal);
        var media = new List<FileSample>();
        var problems = new List<ScanProblem>();
        var seen = 0;

        foreach (var (path, size) in EnumerateFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var kind = MediaTypes.Classify(path);
            var current = totals[kind];
            totals[kind] = new KindTotals(current.Files + 1, current.Bytes + size);

            var extension = Path.GetExtension(path).ToLowerInvariant();
            filesByExtension[extension] = filesByExtension.GetValueOrDefault(extension) + 1;

            if (kind is MediaKind.Photo or MediaKind.Video)
            {
                media.Add(new FileSample(path, size));
                var year = ReadYear(path, problems);
                mediaByYear[year] = mediaByYear.GetValueOrDefault(year) + 1;
            }

            if (++seen % 500 == 0)
            {
                filesSeen?.Report(seen);
            }
        }

        IReadOnlyList<DuplicateGroup>? duplicates = null;
        if (options.FindDuplicates)
        {
            var report = DuplicateFinder.Find(media, cancellationToken);
            duplicates = report.Groups;
            problems.AddRange(report.Unreadable.Select(path => new ScanProblem(path, "Could not be read while looking for duplicates.")));
        }

        return new ScanResult
        {
            Root = root,
            Duration = stopwatch.Elapsed,
            Totals = totals,
            MediaByYear = mediaByYear,
            FilesByExtension = filesByExtension,
            Duplicates = duplicates,
            Problems = problems,
        };
    }

    private static string ReadYear(string path, List<ScanProblem> problems)
    {
        try
        {
            var taken = CaptureDateReader.TryRead(path);
            return taken?.Year.ToString(CultureInfo.InvariantCulture) ?? ScanResult.UnknownYear;
        }
        catch (Exception ex)
        {
            // A damaged file must not stop a scan of the whole drive; it is listed instead.
            problems.Add(new ScanProblem(path, ex.Message));
            return ScanResult.UnknownYear;
        }
    }

    private static FileSystemEnumerable<(string Path, long Size)> EnumerateFiles(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        return new FileSystemEnumerable<(string, long)>(
            root,
            (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.Length),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !IgnoredFolders.Contains(entry.FileName.ToString()),
        };
    }
}
