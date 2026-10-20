using System.Globalization;
using System.Text.RegularExpressions;

namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Reads the ISO 6709 position phones write into videos, such as <c>+38.7223-009.1393+010.000/</c>: in the
/// QuickTime <c>©xyz</c> atom (Android, older iPhones), the <c>com.apple.quicktime.location.ISO6709</c> key
/// (iPhones) or the 3GPP <c>loci</c> box, which ffprobe reports as <c>location</c>.
/// </summary>
public static partial class Iso6709
{
    /// <summary>
    /// Latitude and longitude in decimal degrees, rounded to 6 places as EXIF positions are. Null for anything else:
    /// the degrees-and-minutes forms (<c>+3843.34-00908.36/</c>), coordinates out of range, and
    /// <c>+00.0000+000.0000</c>, which devices write when they had no fix.
    /// </summary>
    public static GeoPoint? Parse(string? value)
    {
        if (value is null)
        {
            return null;
        }

        // Some writers end the atom's text with a C string terminator.
        var match = DecimalDegrees().Match(value.TrimEnd('\0').Trim());
        if (!match.Success
            || !TryParseDegrees(match.Groups["latitude"].Value, out var latitude)
            || !TryParseDegrees(match.Groups["longitude"].Value, out var longitude)
            || Math.Abs(latitude) > 90
            || Math.Abs(longitude) > 180
            || (latitude == 0 && longitude == 0))
        {
            return null;
        }

        return new GeoPoint(latitude, longitude);
    }

    // Rounded before the checks, so a value that rounds to the no-fix position is rejected as one; adding 0 turns -0 into 0.
    private static bool TryParseDegrees(string text, out double degrees)
    {
        var parsed = double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out degrees);
        degrees = Math.Round(degrees, 6) + 0.0;
        return parsed;
    }

    // ISO 6709 fixes the integer digits: 2 for latitude and 3 for longitude in decimal degrees, more in the
    // degrees-and-minutes forms, which this does not read. The altitude, when present, is not used. [0-9], not \d,
    // which also matches other scripts' digits that double.Parse refuses.
    [GeneratedRegex(@"^(?<latitude>[+-][0-9]{2}(?:\.[0-9]+)?)(?<longitude>[+-][0-9]{3}(?:\.[0-9]+)?)(?:[+-][0-9]+(?:\.[0-9]+)?)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalDegrees();
}
