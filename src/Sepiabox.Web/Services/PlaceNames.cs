namespace Sepiabox.Web.Services;

/// <summary>
/// The place line of the detail page: "Matosinhos, Porto, Portugal" (S-012). The names are GeoNames' data, shown
/// as stored and never translated (D-050). A region named like its city, ignoring case ("Lisbon, Lisbon"), is said once.
/// </summary>
public static class PlaceNames
{
    public static string? Format(string? city, string? region, string? country)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return null;
        }

        string?[] parts = [city, string.Equals(region, city, StringComparison.OrdinalIgnoreCase) ? null : region, country];
        return string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
