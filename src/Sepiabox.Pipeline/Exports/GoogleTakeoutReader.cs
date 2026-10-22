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

    // The export's own folders, never an album: "Takeout", "Google Photos" and their translations, with the " (2)" Google adds
    // when an export is split in several.
    [GeneratedRegex(@"^(Takeout|Google Photos|Google Fotos|Google Foto's|Google Foto|Google Zdjęcia)( \(\d+\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Container();

    // The folder of what the person deleted (Takeout calls it Trash; the word is in the language of the account). Its items
    // are not imported: manifests only ever grow (D-036), so a wrong import could not be undone.
    private static readonly HashSet<string> s_trash = Names(
        "Trash", "Bin", "Lixeira", "Reciclagem", "Papierkorb", "Corbeille", "Papelera", "Cestino", "Prullenbak", "Papperskorg",
        "Papirkurv", "Roskakori", "Kosz", "Koš", "Kôš", "Szemetes", "Çöp Kutusu", "Корзина", "ゴミ箱", "휴지통");

    // Archive is a state of a photo, not an album: its photos stay in the library without an album of that name.
    private static readonly HashSet<string> s_archive = Names(
        "Archive", "Arquivo", "Arquivados", "Archiv", "Archivo", "Archivio", "Archief", "Arkiv", "Arkisto", "Archiwum", "Archivovat", "Archívum",
        "Arşiv", "Архив", "アーカイブ", "보관함");

    // Folders of videos Google could not process: not an album either.
    private static readonly HashSet<string> s_failedVideos = Names(
        "Failed Videos", "Vídeos com falha", "Vídeos que falharam", "Fehlgeschlagene Videos", "Vidéos ayant échoué", "Vídeos fallidos");

    // An album whose name has a year is probably a year folder in a language this reader does not know.
    [GeneratedRegex(@"(?<!\d)(19|20)\d\d(?!\d)")]
    private static partial Regex YearInName();

    private static HashSet<string> Names(params string[] names) =>
        new(names.Select(n => n.Normalize(System.Text.NormalizationForm.FormC)), StringComparer.OrdinalIgnoreCase);

    public string Kind => ExportKind;

    public ExportScan Read(string root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!Directory.Exists(root))
        {
            throw new ExportReadException($"The export folder {root} does not exist.");
        }

        var files = new List<KeyValuePair<string, ExportMetadata>>();
        var leftOut = new List<string>();
        var albumNames = new HashSet<string>(StringComparer.Ordinal);
        var withoutSidecar = 0;
        var unreadable = 0;
        var trashed = 0;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var names = Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().ToList();
            var media = names.Where(n => MediaTypes.Classify(n) != MediaKind.Other).Order(StringComparer.Ordinal).ToList();
            if (media.Count == 0)
            {
                continue;
            }

            var folder = Path.GetFileName(directory);
            var inTrash = IsInTrash(root, directory);
            var matches = TakeoutSidecars.Match(media, names.Where(n => !n.Equals("metadata.json", StringComparison.OrdinalIgnoreCase)).ToList());
            var parsed = new Dictionary<string, Sidecar?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in media)
            {
                Sidecar? sidecar = null;
                var matched = matches[name] is not null;
                if (matches[name] is { } json && !parsed.TryGetValue(json, out sidecar))
                {
                    sidecar = ParseSidecar(Path.Combine(directory, json));
                    parsed[json] = sidecar;
                }

                var relative = Path.GetRelativePath(root, Path.Combine(directory, name)).Replace('\\', '/');
                // The sidecar's own flag says whether the photo is in the trash; the folder's name only when it says nothing.
                if (sidecar?.Trashed ?? inTrash)
                {
                    leftOut.Add(relative);
                    trashed++;
                    continue;
                }

                if (!matched)
                {
                    withoutSidecar++;
                }
                else if (sidecar is null)
                {
                    unreadable++;
                }

                var album = Album(root, directory, archived: sidecar?.Archived);
                if (album is not null)
                {
                    albumNames.Add(album);
                }

                var metadata = (sidecar?.Facts ?? new ExportMetadata()) with { Albums = album is null ? [] : [album] };
                files.Add(new KeyValuePair<string, ExportMetadata>(relative, metadata));
            }
        }

        var warnings = new List<string>();
        if (trashed > 0)
        {
            warnings.Add($"{trashed:N0} items in Trash were not imported.");
        }

        if (withoutSidecar > 0)
        {
            warnings.Add($"{withoutSidecar:N0} photos and videos in the export have no JSON sidecar and were read from their own metadata only.");
        }

        if (unreadable > 0)
        {
            warnings.Add($"{unreadable:N0} photos and videos have a JSON sidecar that could not be read; their own metadata was used.");
        }

        var yearLike = albumNames.Where(n => YearInName().IsMatch(n)).Order(StringComparer.Ordinal).ToList();
        if (yearLike.Count > 0)
        {
            var names = $"{string.Join(", ", yearLike.Take(10))}{(yearLike.Count > 10 ? ", ..." : string.Empty)}";
            warnings.Add(yearLike.Count == 1
                ? $"1 album looks like a year folder: {names}. Check it is an album."
                : $"{yearLike.Count:N0} albums look like year folders: {names}. Check they are albums.");
        }

        return new ExportScan(files, leftOut, warnings);
    }

    /// <summary>Whether the folder is, or is inside, a Trash folder.</summary>
    public static bool IsInTrash(string root, string directory) =>
        Path.GetRelativePath(root, directory).Split(['/', '\\']).Any(part => s_trash.Contains(part.Normalize(System.Text.NormalizationForm.FormC)));

    /// <summary>
    /// The album a folder is: its <c>metadata.json</c>'s title, else its name; none for the export's own folders, the
    /// <c>Photos from YYYY</c> folders, the Archive folder (a state, not an album; unless the sidecar says
    /// <paramref name="archived"/> is false) and the folder of failed videos.
    /// </summary>
    public static string? Album(string root, string directory, bool? archived = null)
    {
        if (string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            return null;
        }

        var name = Path.GetFileName(directory).Normalize(System.Text.NormalizationForm.FormC);
        if (Container().IsMatch(name) || YearFolder().IsMatch(name) || s_failedVideos.Contains(name) || (s_archive.Contains(name) && archived != false))
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

    /// <summary>What a sidecar says, and whether it says the photo is in the trash or archived (unconfirmed fields, D-106).</summary>
    private sealed record Sidecar(ExportMetadata Facts, bool? Trashed, bool? Archived);

    private static Sidecar? ParseSidecar(string path)
    {
        if (Parse(path) is not { } facts)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return new Sidecar(facts, Flag(document.RootElement, "trashed"), Flag(document.RootElement, "archived"));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool? Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

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
