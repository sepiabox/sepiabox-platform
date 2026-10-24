using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Exports;

/// <summary>One media file a Meta (Instagram, Facebook) export lists, with what its post said about it.</summary>
/// <param name="Uri">The file's path as the export writes it (relative to the download's root), text-repaired.</param>
/// <param name="RawUri">The path exactly as written, in case the file's name is on disk as written.</param>
/// <param name="Title">The caption of the media, else of the post it is in; null for none.</param>
/// <param name="PostedAt">The creation timestamp (UTC epoch seconds) of the media, else of its post.</param>
/// <param name="Exif">The first <c>exif_data</c> entry of the media with a place or a date.</param>
/// <param name="Text">The media's <c>description</c>, else the text of the post it is in (<c>post</c>), as Facebook writes them; null for none.</param>
public sealed record MetaMedia(string Uri, string RawUri, string? Title, DateTime? PostedAt, MetaExif? Exif, string? Text = null);

/// <summary>What <c>media_metadata</c> says: where and when the camera says the picture was taken.</summary>
public sealed record MetaExif(GeoPoint? Location, DateTime? TakenAt);

/// <summary>
/// What a Meta download's lists say about one media file, merged over the lists it is in (a post and an album): the first
/// caption, date and place found, and every album.
/// </summary>
public sealed record MetaFacts(string? Caption, DateTime? PostedAt, MetaExif? Exif, IReadOnlyList<string> Albums)
{
    public MetaFacts Merge(MetaFacts other) =>
        new(
            Caption ?? other.Caption,
            PostedAt ?? other.PostedAt,
            new MetaExif(Exif?.Location ?? other.Exif?.Location, Exif?.TakenAt ?? other.Exif?.TakenAt),
            [.. Albums.Concat(other.Albums).Distinct(StringComparer.Ordinal)]);

    public ExportMetadata ToMetadata() => new()
    {
        // The camera's date when the list has one; otherwise the posting time, an approximation of the capture date.
        CapturedAt = Exif?.TakenAt ?? PostedAt,
        Location = Exif?.Location,
        Caption = Caption,
        Albums = Albums,
    };
}

/// <summary>
/// The JSON helpers the Meta exports share (S-048, S-049, D-111): finding the media in a post list whatever its nesting,
/// timestamps, <c>exif_data</c> and the repair of Meta's escaped UTF-8 text.
/// </summary>
public static partial class MetaExport
{
    // A guard on the recursion only: JsonDocument parsed with its default options already refuses documents deeper than 64, which
    // a reader reports as an unreadable list.
    private const int MaxDepth = 64;

    [GeneratedRegex(@"^(\d{4})[:\-](\d\d)[:\-](\d\d)[T ](\d\d):(\d\d):(\d\d)(?:[.,]\d+)?\s*(Z|[+-]\d\d(?::?\d\d)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ExifDate();

    /// <summary>
    /// Meta writes the UTF-8 bytes of a text as <c>\u00XX</c> escapes, so "aniversário" reads "aniversÃ¡rio". The text is
    /// reinterpreted as Latin-1 bytes and decoded as UTF-8, and used only when every character is a byte and the bytes are
    /// valid UTF-8; any other text (already right, or with a character above U+00FF) is returned as it is.
    /// </summary>
    public static string RepairText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var needsRepair = false;
        foreach (var c in text)
        {
            if (c > 0xFF)
            {
                return text;
            }

            needsRepair |= c >= 0x80;
        }

        if (!needsRepair)
        {
            return text;
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(Encoding.Latin1.GetBytes(text));
        }
        catch (DecoderFallbackException)
        {
            return text;
        }
    }

    /// <summary>UTC epoch seconds, as a number or a string (a sign allowed), as a UTC date; null when absent or not a date.</summary>
    public static DateTime? Epoch(JsonElement element)
    {
        long? seconds = element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };

        // Far outside what DateTime holds is not a date.
        return seconds is >= -62135596800 and <= 253402300799 ? DateTime.UnixEpoch.AddSeconds(seconds.Value) : null;
    }

    /// <summary>
    /// A camera date as export metadata: <c>2019:07:14 16:30:05</c> or ISO 8601, wall-clock time as written (an offset is
    /// dropped; <c>Z</c> is UTC). Null for anything else.
    /// </summary>
    public static DateTime? ExifTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || ExifDate().Match(text.Trim()) is not { Success: true } m)
        {
            return null;
        }

        var p = Enumerable.Range(1, 6).Select(g => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture)).ToArray();
        try
        {
            var kind = string.Equals(m.Groups[7].Value, "Z", StringComparison.OrdinalIgnoreCase) ? DateTimeKind.Utc : DateTimeKind.Unspecified;
            return new DateTime(p[0], p[1], p[2], p[3], p[4], p[5], kind);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// The place and date in <c>media_metadata</c> (<c>photo_metadata.exif_data[]</c> or <c>video_metadata.exif_data[]</c>):
    /// from the first entry that has a usable value of each; null when it has neither.
    /// </summary>
    public static MetaExif? Exif(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        GeoPoint? location = null;
        DateTime? taken = null;
        DateTime? stamped = null;
        foreach (var key in new[] { "photo_metadata", "video_metadata" })
        {
            if (!metadata.TryGetProperty(key, out var kind) || kind.ValueKind != JsonValueKind.Object
                || !kind.TryGetProperty("exif_data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in data.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                if (location is null && Number(entry, "latitude") is { } lat && Number(entry, "longitude") is { } lon)
                {
                    location = new GeoPoint(lat, lon);
                }

                if (taken is null && entry.TryGetProperty("date_time_original", out var original) && original.ValueKind == JsonValueKind.String)
                {
                    taken = ExifTime(original.GetString());
                }

                // Facebook writes the time the picture was taken as UTC epoch seconds; a camera date in any entry is preferred.
                if (stamped is null && entry.TryGetProperty("taken_timestamp", out var stamp))
                {
                    stamped = Epoch(stamp);
                }
            }
        }

        taken ??= stamped;
        return location is null && taken is null ? null : new MetaExif(location, taken);
    }

    /// <summary>
    /// Every media item in a post list: an object with a <c>uri</c> string. Posts nest differently from one list and version
    /// to another (an array of posts, <c>{"ig_stories": [...]}</c>, posts with a <c>media</c> array), so the document is
    /// walked: a media item takes its own <c>title</c> and <c>creation_timestamp</c> when it has them, else those of the
    /// nearest enclosing object that has them.
    /// </summary>
    public static IEnumerable<MetaMedia> Items(JsonElement root) => Items(root, null, null);

    /// <summary>
    /// As <see cref="Items(JsonElement)"/>, for the media inside one post whose text and time are given: what the media
    /// does not say itself falls back to them.
    /// </summary>
    public static IEnumerable<MetaMedia> Items(JsonElement root, string? postText, DateTime? postedAt)
    {
        var found = new List<MetaMedia>();
        Walk(root, null, postedAt, postText, found, 0);
        return found;
    }

    private static void Walk(JsonElement element, string? title, DateTime? postedAt, string? inheritedText, List<MetaMedia> found, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, title, postedAt, inheritedText, found, depth + 1);
                }

                break;
            case JsonValueKind.Object:
                var ownTitle = element.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } text
                    ? RepairText(text)
                    : title;
                var ownText = Text(element, "description") ?? Text(element, "post") ?? inheritedText;
                var ownTime = element.TryGetProperty("creation_timestamp", out var c) && Epoch(c) is { } time ? time : postedAt;
                if (element.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String && uri.GetString() is { Length: > 0 } raw)
                {
                    var metadata = element.TryGetProperty("media_metadata", out var m) ? Exif(m) : null;
                    found.Add(new MetaMedia(RepairText(raw), raw, ownTitle, ownTime, metadata, ownText));
                    break;
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    {
                        Walk(property.Value, ownTitle, ownTime, ownText, found, depth + 1);
                    }
                }

                break;
            default:
                break;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Trim().Length > 0
            ? RepairText(text)
            : null;

    /// <summary>
    /// The media file's path under the scan root, as written or text-repaired, whichever exists; null when neither does, when
    /// the path leaves the download or when it is not a photo or a video. <paramref name="baseFolder"/> is the download's root
    /// under <paramref name="root"/> (empty, or ending in a slash).
    /// </summary>
    public static string? Resolve(string root, string baseFolder, MetaMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);
        foreach (var uri in new[] { media.RawUri, media.Uri }.Distinct(StringComparer.Ordinal))
        {
            // A NUL is never in a real path and makes the framework throw; the entry is one more that is not in the download.
            if (uri.Contains('\0', StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var relative = (baseFolder + uri.Replace('\\', '/').TrimStart('/')).Replace("//", "/", StringComparison.Ordinal);
                var full = Path.GetFullPath(Path.Combine(root, relative));
                var inside = Path.GetRelativePath(root, full);
                // Nothing outside the download is ever read, whatever the list says: ".." parts, an absolute path on another drive.
                if (inside == ".." || inside.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(inside))
                {
                    continue;
                }

                if (File.Exists(full) && IsMedia(full))
                {
                    return inside.Replace('\\', '/');
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A path the file system cannot take (illegal characters, too long) is not a file of the download.
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="path"/> is a photo or video file by its extension.</summary>
    public static bool IsMedia(string path) => MediaTypes.Classify(path) != MediaKind.Other;

    private static double? Number(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
    }
}
