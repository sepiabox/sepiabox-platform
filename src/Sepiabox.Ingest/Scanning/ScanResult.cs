using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Scanning;

public sealed record KindTotals(int Files, long Bytes);

public sealed record ScanProblem(string Path, string Reason);

public sealed class ScanResult
{
    public const string UnknownYear = "unknown";

    public required string Root { get; init; }

    public required TimeSpan Duration { get; init; }

    public required IReadOnlyDictionary<MediaKind, KindTotals> Totals { get; init; }

    /// <summary>Photos and videos per year taken, from their metadata; <see cref="UnknownYear"/> when they do not say.</summary>
    public required IReadOnlyDictionary<string, int> MediaByYear { get; init; }

    public required IReadOnlyDictionary<string, int> FilesByExtension { get; init; }

    /// <summary>Null when duplicate detection was skipped.</summary>
    public required IReadOnlyList<DuplicateGroup>? Duplicates { get; init; }

    public required IReadOnlyList<ScanProblem> Problems { get; init; }

    public int RedundantCopies => Duplicates?.Sum(group => group.Paths.Count - 1) ?? 0;

    public long RedundantBytes => Duplicates?.Sum(group => group.RedundantBytes) ?? 0;
}
