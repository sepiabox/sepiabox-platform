using System.Globalization;

namespace Sepiabox.Web.Services;

/// <summary>How a video's length is written on cards and the detail page (D-049).</summary>
public static class VideoDurations
{
    // About 31 years: beyond any real video, and far inside what TimeSpan can hold.
    private const double MaxSeconds = 1e9;

    /// <summary>
    /// <c>m:ss</c>, or <c>h:mm:ss</c> from one hour on, in whole seconds rounded down as browsers' players
    /// show them. Null for an unknown, zero, negative, non-finite or absurd length, which shows nothing
    /// rather than a wrong time (a 0-second "video" is a probe that found no length).
    /// </summary>
    public static string? Format(double? seconds)
    {
        if (seconds is not { } value || !double.IsFinite(value) || value is <= 0 or > MaxSeconds)
        {
            return null;
        }

        var total = TimeSpan.FromSeconds(Math.Floor(value));
        return total.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(long)total.TotalHours}:{total.Minutes:00}:{total.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{total.Minutes}:{total.Seconds:00}");
    }
}
