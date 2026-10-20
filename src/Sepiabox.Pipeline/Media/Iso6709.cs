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

        var match = DecimalDegrees().Match(value.Trim());
        if (!match.Success)
        {
            return null;
        }

        var latitude = double.Parse(match.Groups["latitude"].Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var longitude = double.Parse(match.Groups["longitude"].Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180 || (latitude == 0 && longitude == 0))
        {
            return null;
        }

        return new GeoPoint(Math.Round(latitude, 6), Math.Round(longitude, 6));
    }

    // ISO 6709 fixes the integer digits: 2 for latitude and 3 for longitude in decimal degrees, more in the
    // degrees-and-minutes forms, which this does not read. The altitude, when present, is not used.
    [GeneratedRegex(@"^(?<latitude>[+-]\d{2}(?:\.\d+)?)(?<longitude>[+-]\d{3}(?:\.\d+)?)(?:[+-]\d+(?:\.\d+)?)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalDegrees();
}
