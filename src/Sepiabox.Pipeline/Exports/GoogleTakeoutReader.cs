using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Exports;

/// <summary>
/// Reads an extracted Google Takeout export of Google Photos (S-046, D-053, D-106): <c>process --export google-takeout</c>.
/// One JSON sidecar per media file holds when it was taken (<c>photoTakenTime</c>, UTC epoch seconds), where
/// (<c>geoData</c>, else <c>geoDataExif</c>), what the person wrote (<c>description</c>) and its address in Google Photos
/// (<c>url</c>); the album is the folder the file is in. The export is only read.
/// </summary>
public sealed partial class GoogleTakeoutReader : IExportReader
{
    public const string ExportKind = "google-takeout";

    // The year folders of an account in the languages Takeout was seen in; any other language's would show up as an album.
    [GeneratedRegex(@"^(Photos from|Fotos de|Fotos von|Photos de|Foto's uit|Foto del|Foto dal|Zdjęcia z|Fotky z|Foton från) (19|20)\d\d$", RegexOptions.IgnoreCase)]
    private static partial Regex YearFolder();

    // Folders that are the export's own, never an album.
    private static readonly HashSet<string> s_containers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Takeout", "Google Photos", "Google Fotos", "Google Foto's", "Google Photos (1)",
    };

    public string Kind => ExportKind;

    public ExportScan Read(string root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!Directory.Exists(root))
        {
            throw new ExportReadException($"The export folder {root} does not exist.");
        }

        var files = new List<KeyValuePair<string, ExportMetadata>>();
        var withoutSidecar = 0;
        var unreadable = 0;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var names = Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().ToList();
            var media = names.Where(n => MediaTypes.Classify(n) != MediaKind.Other).Order(StringComparer.Ordinal).ToList();
            if (media.Count == 0)
            {
                continue;
            }

            var album = Album(root, directory);
            var matches = TakeoutSidecars.Match(media, names.Where(n => !n.Equals("metadata.json", StringComparison.OrdinalIgnoreCase)).ToList());
            var parsed = new Dictionary<string, ExportMetadata?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in media)
            {
                ExportMetadata? sidecar = null;
                if (matches[name] is { } json)
                {
                    if (!parsed.TryGetValue(json, out sidecar))
                    {
                        sidecar = Parse(Path.Combine(directory, json));
                        parsed[json] = sidecar;
                    }

                    if (sidecar is null)
                    {
                        unreadable++;
                    }
                }
                else
                {
                    withoutSidecar++;
                }

                var metadata = (sidecar ?? new ExportMetadata()) with { Albums = album is null ? [] : [album] };
                var relative = Path.GetRelativePath(root, Path.Combine(directory, name)).Replace('\\', '/');
                files.Add(new KeyValuePair<string, ExportMetadata>(relative, metadata));
            }
        }

        var warnings = new List<string>();
        if (withoutSidecar > 0)
        {
            warnings.Add($"{withoutSidecar:N0} photos and videos in the export have no JSON sidecar and were read from their own metadata only.");
        }

        if (unreadable > 0)
        {
            warnings.Add($"{unreadable:N0} photos and videos have a JSON sidecar that could not be read; their own metadata was used.");
        }

        return new ExportScan(files, warnings: warnings);
    }

    /// <summary>
    /// The album a folder is: its <c>metadata.json</c>'s title, else its name; none for the export's own folders and the
    /// <c>Photos from YYYY</c> folders.
    /// </summary>
    public static string? Album(string root, string directory)
    {
        if (string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            return null;
        }

        var name = Path.GetFileName(directory);
        if (s_containers.Contains(name) || YearFolder().IsMatch(name))
        {
            return null;
        }

        var title = AlbumTitle(Path.Combine(directory, "metadata.json"));
        return title is not null && !YearFolder().IsMatch(title) ? title : name;
    }

    private static string? AlbumTitle(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                && title.GetString()?.Trim() is { Length: > 0 } text
                    ? text
                    : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>What a sidecar says, or null when it cannot be read as one.</summary>
    public static ExportMetadata? Parse(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new ExportMetadata
            {
                CapturedAt = TakenTime(root),
                Location = Location(root, "geoData") is { } geo && !IsNone(geo) ? geo : Location(root, "geoDataExif"),
                Caption = Text(root, "description"),
                SourceUrl = Text(root, "url"),
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // photoTakenTime.timestamp: UTC epoch seconds, written as a string.
    private static DateTime? TakenTime(JsonElement root)
    {
        if (!root.TryGetProperty("photoTakenTime", out var taken) || taken.ValueKind != JsonValueKind.Object
            || !taken.TryGetProperty("timestamp", out var stamp))
        {
            return null;
        }

        var seconds = stamp.ValueKind switch
        {
            JsonValueKind.String when long.TryParse(stamp.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var s) => s,
            JsonValueKind.Number when stamp.TryGetInt64(out var n) => n,
            _ => (long?)null,
        };

        // Far outside what DateTime holds is not a date.
        return seconds is { } value && value is >= -62_135_596_800 and <= 253_402_300_799
            ? DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime
            : null;
    }

    private static Sepiabox.Pipeline.Media.GeoPoint? Location(JsonElement root, string name) =>
        root.TryGetProperty(name, out var geo) && geo.ValueKind == JsonValueKind.Object
        && Number(geo, "latitude") is { } latitude && Number(geo, "longitude") is { } longitude
            ? new Sepiabox.Pipeline.Media.GeoPoint(latitude, longitude)
            : null;

    private static bool IsNone(Sepiabox.Pipeline.Media.GeoPoint point) => point.Latitude == 0 && point.Longitude == 0;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDouble(out var d) => d,
                JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
                _ => null,
            }
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
