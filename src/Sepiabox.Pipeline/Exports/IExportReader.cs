using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Exports;

/// <summary>
/// Reads what an official export (Google Takeout, Apple Photos, Instagram, Facebook) says about its media files (S-045,
/// D-053): dates, places, captions and albums that the files themselves often lack. One reader per export kind; the
/// readers themselves come with S-046 to S-049. A reader is pure over the folder and read-only: it never opens a file
/// for writing (CLAUDE.md).
/// </summary>
public interface IExportReader
{
    /// <summary>The name given to <c>process --export</c>, lower case: <c>google-takeout</c>, <c>apple-photos</c> and so on.</summary>
    string Kind { get; }

    /// <summary>What the export at <paramref name="root"/> says, or <see cref="ExportReadException"/> when it cannot be read.</summary>
    ExportScan Read(string root, CancellationToken cancellationToken = default);
}

/// <summary>
/// What an export says about one media file. Every part is optional.
/// </summary>
public sealed record ExportMetadata
{
    /// <summary>
    /// When the file was taken. A <see cref="DateTimeKind.Utc"/> value is UTC and becomes wall-clock time in the zone given
    /// to <c>process --time-zone</c>; any other kind, <see cref="DateTimeKind.Local"/> and <see cref="DateTimeKind.Unspecified"/> alike, is already wall-clock time and is used as it is (D-037), so a reader must not hand over a time converted to the computer's zone.
    /// </summary>
    public DateTime? CapturedAt { get; init; }

    /// <summary>Where it was taken; coordinates 0,0 or outside the globe count as none (D-104).</summary>
    public GeoPoint? Location { get; init; }

    public string? Caption { get; init; }

    public IReadOnlyList<string> Albums { get; init; } = [];
}

/// <summary>
/// The result of reading an export: what it says about each media file, by path relative to the export's root, and the
/// media-looking files that are export bookkeeping (thumbnails, previews of posts) and must not be catalogued.
/// </summary>
public sealed class ExportScan
{
    private readonly Dictionary<string, ExportMetadata> _files;
    private readonly HashSet<string> _leftOut;

    public ExportScan(IEnumerable<KeyValuePair<string, ExportMetadata>>? files = null, IEnumerable<string>? leftOut = null)
    {
        // Slashes and case do not matter for finding a file: a sidecar names it as the export's maker spelled it.
        _files = new Dictionary<string, ExportMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, metadata) in files ?? [])
        {
            _files[Normalize(path)] = metadata;
        }

        _leftOut = new HashSet<string>((leftOut ?? []).Select(Normalize), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The number of files the export describes.</summary>
    public int Count => _files.Count;

    /// <summary>What the export says about the file at <paramref name="relativePath"/>, or null.</summary>
    public ExportMetadata? Find(string relativePath) => _files.GetValueOrDefault(Normalize(relativePath));

    /// <summary>Whether the file at <paramref name="relativePath"/> is export bookkeeping, to be left out.</summary>
    public bool IsLeftOut(string relativePath) => _leftOut.Contains(Normalize(relativePath));

    // Also in normalization form C: a Mac writes file names in form D, and a sidecar names them in form C.
    private static string Normalize(string path) => path.Normalize(System.Text.NormalizationForm.FormC).Replace('\\', '/').TrimStart('/');
}

/// <summary>The export could not be read as the kind asked for; <c>process</c> stops before reading any file.</summary>
public sealed class ExportReadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The export readers <c>process --export</c> knows (S-045). S-046 to S-049 each add theirs to <see cref="Default"/>.</summary>
public sealed class ExportReaderRegistry
{
    private readonly Dictionary<string, IExportReader> _readers;

    public ExportReaderRegistry(IEnumerable<IExportReader> readers)
    {
        ArgumentNullException.ThrowIfNull(readers);
        _readers = readers.ToDictionary(r => r.Kind, StringComparer.Ordinal);
    }

    /// <summary>The readers shipped with the tool: none yet.</summary>
    public static ExportReaderRegistry Default { get; } = new([]);

    /// <summary>The kinds <c>--export</c> accepts, in order.</summary>
    public IReadOnlyList<string> Kinds => [.. _readers.Keys.Order(StringComparer.Ordinal)];

    public bool TryGet(string kind, out IExportReader reader)
    {
        var found = _readers.TryGetValue(kind, out var value);
        reader = value!;
        return found;
    }
}
