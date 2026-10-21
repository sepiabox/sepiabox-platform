using System.Globalization;

namespace Sepiabox.Web.Services;

/// <summary>
/// A place's coordinates as the family types or pastes them (S-014): latitude then longitude, such as "38.72, -9.13"
/// from a map. With decimal points, a comma or semicolon separates the two; without, a decimal comma is allowed and a
/// space or semicolon separates them ("38,72 -9,13").
/// </summary>
public static class PlaceCoordinates
{
    public static (double Latitude, double Longitude)? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        string[] parts = value.Contains('.', StringComparison.Ordinal)
            ? value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : value.Contains(';', StringComparison.Ordinal)
                ? value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts is [var latitude, var longitude] && Number(latitude) is { } lat && Number(longitude) is { } lon
            && lat is >= -90 and <= 90 && lon is >= -180 and <= 180
            ? (lat, lon)
            : null;
    }

    private static double? Number(string value) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
