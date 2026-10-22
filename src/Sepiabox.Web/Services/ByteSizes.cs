using System.Globalization;

namespace Sepiabox.Web.Services;

/// <summary>A size in bytes for people (S-028): "512 B", "1.5 MB", "2.4 GB", with the current culture's decimal mark.</summary>
public static class ByteSizes
{
    private static readonly string[] s_units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (unit < s_units.Length - 1 && (value >= 1024 || (unit > 0 && Math.Round(value, 1, MidpointRounding.AwayFromZero) >= 1024)))
        {
            // Also when it only rounds up to 1024 ("1023.99 KB" is "1 MB", not "1024 KB").
            value /= 1024;
            unit++;
        }

        // Whole bytes have no fraction; the rest keep one digit, dropped when it is zero ("2 GB", "2.4 GB").
        var number = unit == 0 ? value.ToString("N0", CultureInfo.CurrentCulture) : Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.CurrentCulture);
        return $"{number} {s_units[unit]}";
    }
}
