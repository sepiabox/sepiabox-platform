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
public sealed record MetaMedia(string Uri, string RawUri, string? Title, DateTime? PostedAt, MetaExif? Exif);

/// <summary>What <c>media_metadata</c> says: where and when the camera says the picture was taken.</summary>
public sealed record MetaExif(GeoPoint? Location, DateTime? TakenAt);

/// <summary>
/// The JSON helpers the Meta exports share (S-048, S-049, D-111): finding the media in a post list whatever its nesting,
/// timestamps, <c>exif_data</c> and the repair of Meta's escaped UTF-8 text.
/// </summary>
public static partial class MetaExport
{
    /// <summary>The deepest nesting <see cref="Items(JsonElement, Action?)"/> follows; JSON parsed with the default options cannot be deeper than 64.</summary>
    public const int MaxDepth = 64;

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
            }
        }

        return location is null && taken is null ? null : new MetaExif(location, taken);
    }

    /// <summary>
    /// Every media item in a post list: an object with a <c>uri</c> string. Posts nest differently from one list and version
    /// to another (an array of posts, <c>{"ig_stories": [...]}</c>, posts with a <c>media</c> array), so the document is
    /// walked: a media item takes its own <c>title</c> and <c>creation_timestamp</c> when it has them, else those of the
    /// nearest enclosing object that has them.
    /// </summary>
    public static IEnumerable<MetaMedia> Items(JsonElement root, Action? tooDeep = null)
    {
        var found = new List<MetaMedia>();
        Walk(root, null, null, found, 0, tooDeep);
        return found;
    }

    private static void Walk(JsonElement element, string? title, DateTime? postedAt, List<MetaMedia> found, int depth, Action? tooDeep)
    {
        if (depth > MaxDepth)
        {
            tooDeep?.Invoke();
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, title, postedAt, found, depth + 1, tooDeep);
                }

                break;
            case JsonValueKind.Object:
                var ownTitle = element.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } text
                    ? RepairText(text)
                    : title;
                var ownTime = element.TryGetProperty("creation_timestamp", out var c) && Epoch(c) is { } time ? time : postedAt;
                if (element.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String && uri.GetString() is { Length: > 0 } raw)
                {
                    var metadata = element.TryGetProperty("media_metadata", out var m) ? Exif(m) : null;
                    found.Add(new MetaMedia(RepairText(raw), raw, ownTitle, ownTime, metadata));
                    break;
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    {
                        Walk(property.Value, ownTitle, ownTime, found, depth + 1, tooDeep);
                    }
                }

                break;
            default:
                break;
        }
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
