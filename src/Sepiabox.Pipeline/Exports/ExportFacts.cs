using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Exports;

/// <summary>
/// What <c>process</c> takes from an <see cref="ExportMetadata"/> for a manifest (S-045, D-104): a wall-clock date, a
/// usable location, a caption that fits the schema and a clean list of albums.
/// </summary>
public static class ExportFacts
{
    /// <summary>The longest caption a manifest holds; longer ones are cut.</summary>
    public const int MaxCaptionLength = 2000;

    /// <summary>The export's date as wall-clock time (D-037): UTC converted to <paramref name="zone"/>, anything else as it is.</summary>
    public static DateTime? WallClock(ExportMetadata? export, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (export?.CapturedAt is not { } at)
        {
            return null;
        }

        return at.Kind == DateTimeKind.Utc
            ? DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(at, zone), DateTimeKind.Unspecified)
            : DateTime.SpecifyKind(at, DateTimeKind.Unspecified);
    }

    /// <summary>The export's location when it is on the globe and not 0,0 (a device's "no fix"), otherwise null.</summary>
    public static GeoPoint? Location(ExportMetadata? export) =>
        export?.Location is { } point
        && double.IsFinite(point.Latitude) && double.IsFinite(point.Longitude)
        && point.Latitude is >= -90 and <= 90 && point.Longitude is >= -180 and <= 180
        && !(point.Latitude == 0 && point.Longitude == 0)
            ? point
            : null;

    /// <summary>The caption trimmed and cut to <see cref="MaxCaptionLength"/> characters; null when nothing is left.</summary>
    public static string? Caption(ExportMetadata? export)
    {
        var text = export?.Caption?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length <= MaxCaptionLength)
        {
            return text;
        }

        // Not in the middle of a surrogate pair.
        var length = char.IsHighSurrogate(text[MaxCaptionLength - 1]) ? MaxCaptionLength - 1 : MaxCaptionLength;
        return text[..length].TrimEnd();
    }

    /// <summary>The albums trimmed, without empty names or repeats, in ordinal order; null when there are none.</summary>
    public static IReadOnlyList<string>? Albums(ExportMetadata? export) =>
        export is null ? null : Clean(export.Albums);

    /// <summary>A list of album names as a manifest keeps it: trimmed, distinct, ordinal order; null for none.</summary>
    public static IReadOnlyList<string>? Clean(IEnumerable<string>? names)
    {
        var cleaned = (names ?? []).Select(n => n?.Trim()).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return cleaned.Count == 0 ? null : cleaned;
    }
}
