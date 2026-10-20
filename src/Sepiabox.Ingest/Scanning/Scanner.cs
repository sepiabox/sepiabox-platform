using System.Diagnostics;
using System.Globalization;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Scanning;

/// <summary>
/// Reads a folder tree without changing anything in it and reports what it holds.
/// Files are read one at a time, which is what a spinning external drive handles best.
/// </summary>
public static class Scanner
{
    public static ScanResult Scan(ScanOptions options, IProgress<int>? filesSeen = null, CancellationToken cancellationToken = default) =>
        Scan(options, filesSeen, TimeProvider.System, cancellationToken);

    /// <summary>The same with <paramref name="clock"/> as "now" for the plausibility of capture dates (D-046).</summary>
    internal static ScanResult Scan(
        ScanOptions options, IProgress<int>? filesSeen, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        var root = FileTree.ResolveRoot(options.Root);

        var stopwatch = Stopwatch.StartNew();
        var totals = Enum.GetValues<MediaKind>().ToDictionary(kind => kind, _ => new KindTotals(0, 0));
        var mediaByYear = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var filesByExtension = new Dictionary<string, int>(StringComparer.Ordinal);
        var media = new List<FileSample>();
        var problems = new List<ScanProblem>();
        var seen = 0;

        foreach (var (path, size, _) in FileTree.Enumerate(root))
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
                var year = ReadYear(path, clock, problems);
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

    // The same rule as process (D-046), so "by year" matches the dates process will record.
    private static string ReadYear(string path, TimeProvider clock, List<ScanProblem> problems)
    {
        DateTime? fromMetadata = null;
        try
        {
            fromMetadata = CaptureDateReader.TryRead(path);
        }
        catch (Exception ex)
        {
            // A damaged file must not stop a scan of the whole drive; it is listed, and its path may still date it.
            problems.Add(new ScanProblem(path, ex.Message));
        }

        var taken = CaptureDate.Resolve(fromMetadata, path, clock)?.Value;
        return taken?.Year.ToString(CultureInfo.InvariantCulture) ?? ScanResult.UnknownYear;
    }
}
