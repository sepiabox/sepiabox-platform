using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Exports;

/// <summary>
/// Reads an Apple Photos or iCloud Photos export (S-047, D-053, D-109): <c>process --export apple-photos</c>. It reads
/// whichever of the two layouts the folder holds, or both:
/// <list type="bullet">
/// <item>The Photos app's "Export Unmodified Originals": the originals and, with "Export IPTC as XMP", an <c>.xmp</c> sidecar
/// per file (<see cref="AppleXmp"/>) holding the description, the date and the GPS position. Moment-name folders are not albums.</item>
/// <item>The privacy.apple.com copy: <c>Photo Details*.csv</c> (date and the <c>deleted</c> flag per image name) and an
/// <c>Albums</c> folder with one CSV per album listing image names.</item>
/// </list>
/// Apple documents none of these files, so every assumption about them is in D-109 and is to be confirmed with a real export.
/// The export is only read.
/// </summary>
public sealed partial class AppleExportReader : IExportReader
{
    public const string ExportKind = "apple-photos";

    private const long MaxXmpBytes = 4 * 1024 * 1024;

    // Values of the deleted column that mean "not deleted"; any other text is taken as deleted, because an import cannot be
    // undone (D-036) and a left-out file is only a rerun away.
    private static readonly HashSet<string> s_notDeleted = new(["", "no", "false", "0", "n", "-"], StringComparer.OrdinalIgnoreCase);

    // "Wednesday May 23,2018 4:16 PM GMT": the weekday is dropped, it adds nothing and a wrong one would fail the parse.
    [GeneratedRegex(@"^[A-Za-z]+,?\s+(?=[A-Za-z]+\.?\s+\d)")]
    private static partial Regex LeadingWeekday();

    private static readonly string[] s_dateFormats =
    [
        "MMMM d,yyyy h:mm tt", "MMMM d, yyyy h:mm tt", "MMMM d,yyyy h:mm:ss tt", "MMMM d, yyyy h:mm:ss tt",
        "MMMM d,yyyy H:mm", "MMMM d, yyyy H:mm", "MMMM d,yyyy H:mm:ss", "MMMM d, yyyy H:mm:ss",
        "MMM d,yyyy h:mm tt", "MMM d, yyyy h:mm tt", "MMM d,yyyy h:mm:ss tt", "MMM d, yyyy h:mm:ss tt",
    ];

    public string Kind => ExportKind;

    /// <summary>
    /// The <c>originalCreationDate</c> of a details row: the believed form "Wednesday May 23,2018 4:16 PM GMT" and the same
    /// with a space after the comma, seconds or a 24-hour clock; ISO 8601 as well. A trailing <c>GMT</c>, <c>UTC</c> or
    /// <c>Z</c> makes it UTC (converted to the time zone given to <c>process</c>), no zone makes it wall-clock time. Null for
    /// any other form: a numeric day-month order would be a guess, so it is not read.
    /// </summary>
    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = Regex.Replace(text.Trim(), @"\s+", " ");
        if (AppleXmp.Date(value) is { } iso)
        {
            return iso;
        }

        var utc = false;
        foreach (var suffix in new[] { " GMT", " UTC" })
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^suffix.Length];
                utc = true;
                break;
            }
        }

        value = LeadingWeekday().Replace(value, string.Empty);
        return DateTime.TryParseExact(value, s_dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? DateTime.SpecifyKind(parsed, utc ? DateTimeKind.Utc : DateTimeKind.Unspecified)
            : null;
    }

    public ExportScan Read(string root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!Directory.Exists(root))
        {
            throw new ExportReadException($"The export folder {root} does not exist.");
        }

        var all = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        var media = all.Where(f => MediaTypes.Classify(f) != MediaKind.Other).ToList();
        var byName = media.GroupBy(f => Key(Path.GetFileName(f)), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var xmps = new HashSet<string>(all.Where(f => f.EndsWith(".xmp", StringComparison.OrdinalIgnoreCase)).Select(Key), StringComparer.Ordinal);
        var warnings = new List<string>();

        var details = ReadDetails(root, all, warnings, cancellationToken);
        var albums = ReadAlbums(root, all, byName, warnings, cancellationToken);

        var files = new List<KeyValuePair<string, ExportMetadata>>();
        var leftOut = new List<string>();
        var deleted = 0;
        var ambiguous = 0;
        var unreadableXmp = 0;
        var withoutRow = 0;
        foreach (var path in media)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(Path.GetFileName(path));
            var unique = byName[key].Count == 1;
            DetailsRow? row = null;
            if (details is not null)
            {
                if (!unique)
                {
                    ambiguous++;
                }
                else if (details.TryGetValue(key, out var found))
                {
                    row = found;
                }
                else
                {
                    withoutRow++;
                }
            }

            if (row?.Deleted == true)
            {
                leftOut.Add(path);
                deleted++;
                continue;
            }

            var xmp = ReadXmp(root, path, xmps, ref unreadableXmp);
            var named = unique && albums.TryGetValue(key, out var set) ? set.Order(StringComparer.Ordinal).ToList() : [];
            files.Add(new KeyValuePair<string, ExportMetadata>(path, new ExportMetadata
            {
                CapturedAt = xmp?.CapturedAt ?? row?.CapturedAt,
                Location = xmp?.Location,
                Caption = xmp?.Caption,
                Albums = named,
            }));
        }

        if (deleted > 0)
        {
            warnings.Add($"{deleted:N0} photos and videos are marked deleted in Photo Details and were not imported.");
        }

        if (ambiguous > 0)
        {
            warnings.Add($"{ambiguous:N0} photos and videos share a file name with another in the export, so Photo Details and the album lists were not applied to them.");
        }

        if (withoutRow > 0)
        {
            warnings.Add($"{withoutRow:N0} photos and videos have no row in Photo Details and were read from their own metadata only.");
        }

        if (unreadableXmp > 0)
        {
            warnings.Add($"{unreadableXmp:N0} .xmp sidecars could not be read; the files' own metadata was used.");
        }

        return new ExportScan(files, leftOut, warnings);
    }

    private sealed record DetailsRow(DateTime? CapturedAt, bool Deleted);

    // Photo Details*.csv anywhere in the export, by image name; null when there is none. An image named in several rows with
    // different content is unclear and is left out of the result.
    private static Dictionary<string, DetailsRow>? ReadDetails(string root, List<string> all, List<string> warnings, CancellationToken cancellationToken)
    {
        var csvs = all.Where(f => Path.GetFileName(f).StartsWith("Photo Details", StringComparison.OrdinalIgnoreCase)
            && f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToList();
        if (csvs.Count == 0)
        {
            return null;
        }

        var rows = new Dictionary<string, List<DetailsRow>>(StringComparer.Ordinal);
        var badDates = 0;
        string? sample = null;
        foreach (var csv in csvs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadCsv(Path.Combine(root, csv)) is not { } table)
            {
                warnings.Add($"{csv} could not be read and was ignored.");
                continue;
            }

            var header = table.FirstOrDefault()?.Select(h => h.Trim().Replace(" ", string.Empty, StringComparison.Ordinal)).ToList() ?? [];
            var name = header.FindIndex(h => h.Equals("imgName", StringComparison.OrdinalIgnoreCase));
            var date = header.FindIndex(h => h.Equals("originalCreationDate", StringComparison.OrdinalIgnoreCase));
            var flag = header.FindIndex(h => h.Equals("deleted", StringComparison.OrdinalIgnoreCase));
            if (name < 0)
            {
                warnings.Add($"{csv} has no imgName column and was ignored.");
                continue;
            }

            foreach (var line in table.Skip(1))
            {
                var image = Cell(line, name);
                if (string.IsNullOrWhiteSpace(image))
                {
                    continue;
                }

                var text = Cell(line, date);
                var parsed = ParseDate(text);
                if (parsed is null && !string.IsNullOrWhiteSpace(text))
                {
                    badDates++;
                    sample ??= text.Trim();
                }

                var row = new DetailsRow(parsed, flag >= 0 && !s_notDeleted.Contains(Cell(line, flag).Trim()));
                var key = Key(Path.GetFileName(image.Replace('\\', '/')));
                if (!rows.TryGetValue(key, out var list))
                {
                    rows[key] = list = [];
                }

                list.Add(row);
            }
        }

        if (badDates > 0)
        {
            warnings.Add($"{badDates:N0} originalCreationDate values were not in a known date format (for example \"{sample}\") and were ignored.");
        }

        // A row repeated exactly is one row; rows that disagree say nothing for sure.
        return rows.Where(p => p.Value.Distinct().Count() == 1).ToDictionary(p => p.Key, p => p.Value[0], StringComparer.Ordinal);
    }

    // Albums/<album>.csv anywhere in the export: the album is the file's name, its rows' first cell is an image name.
    private static Dictionary<string, HashSet<string>> ReadAlbums(
        string root, List<string> all, Dictionary<string, List<string>> byName, List<string> warnings, CancellationToken cancellationToken)
    {
        var albums = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var csv in all.Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            && f.Split('/')[..^1].Any(part => part.Equals("Albums", StringComparison.OrdinalIgnoreCase))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var album = Path.GetFileNameWithoutExtension(csv).Normalize(NormalizationForm.FormC).Trim();
            if (ReadCsv(Path.Combine(root, csv)) is not { } table)
            {
                warnings.Add($"{csv} could not be read and was ignored.");
                continue;
            }

            var matched = 0;
            foreach (var line in table)
            {
                var image = Cell(line, 0);
                if (string.IsNullOrWhiteSpace(image) || !byName.ContainsKey(Key(Path.GetFileName(image.Replace('\\', '/')))))
                {
                    continue;
                }

                var key = Key(Path.GetFileName(image.Replace('\\', '/')));
                if (!albums.TryGetValue(key, out var set))
                {
                    albums[key] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(album);
                matched++;
            }

            if (matched == 0 && album.Length > 0)
            {
                warnings.Add($"The album list {csv} names no file of the export; check its layout.");
            }
        }

        return albums;
    }

    // The sidecar of a media file in the same folder: "IMG_0001.HEIC.xmp", else "IMG_0001.xmp" (which a Live Photo's still
    // and video share); null when there is none or it cannot be read.
    private static ExportMetadata? ReadXmp(string root, string path, HashSet<string> xmps, ref int unreadable)
    {
        var directory = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
        var name = Path.GetFileName(path);
        var candidates = new[] { name + ".xmp", Path.GetFileNameWithoutExtension(name) + ".xmp" };
        foreach (var candidate in candidates)
        {
            var relative = directory.Length == 0 ? candidate : directory + "/" + candidate;
            if (!xmps.Contains(Key(relative)))
            {
                continue;
            }

            // The sidecar's real spelling may differ in case from the media file's.
            var actual = Directory.EnumerateFiles(Path.Combine(root, directory)).FirstOrDefault(f => Key(Path.GetFileName(f)) == Key(candidate));
            try
            {
                if (actual is null || new FileInfo(actual).Length > MaxXmpBytes)
                {
                    unreadable++;
                    return null;
                }

                if (AppleXmp.Parse(File.ReadAllText(actual)) is { } facts)
                {
                    return facts;
                }

                unreadable++;
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable++;
                return null;
            }
        }

        return null;
    }

    private static List<List<string>>? ReadCsv(string path)
    {
        try
        {
            return AppleCsv.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Cell(List<string> line, int index) => index >= 0 && index < line.Count ? line[index] : string.Empty;

    // A file name as the exports spell it differently: Unicode form C, case ignored (a CSV is typed on one system, the files on another).
    private static string Key(string name) => name.Normalize(NormalizationForm.FormC).ToLowerInvariant();
}
