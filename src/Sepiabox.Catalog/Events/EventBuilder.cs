namespace Sepiabox.Catalog.Events;

/// <summary>A dated asset as the builder reads it: where and when, and the place the catalog named for it (S-023).</summary>
public sealed record EventAsset(string Id, DateTime CapturedAt, double? Latitude, double? Longitude, string? City = null, string? Country = null);

/// <summary>An occasion the builder found, before it is stored. <see cref="AssetIds"/> are in capture order.</summary>
public sealed record EventDraft(
    DateTime StartsAt, DateTime EndsAt, double? CentroidLatitude, double? CentroidLongitude, string? PlaceLabel, IReadOnlyList<string> AssetIds);

/// <summary>
/// Groups dated assets into occasions (S-023, D-019). The assets are put in order of capture time (then id), and a new
/// occasion starts when the gap to the previous asset is more than <see cref="MaxGap"/>, or when the asset is more than
/// <see cref="MaxDistanceKm"/> from the previous geotagged asset of the occasion. Pure and deterministic: the same
/// assets, in any order, give the same occasions.
/// </summary>
public static class EventBuilder
{
    /// <summary>A gap of exactly this long is still the same occasion.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromHours(6);

    /// <summary>A distance of exactly this many kilometres is still the same occasion.</summary>
    public const double MaxDistanceKm = 50;

    private const double EarthRadiusKm = 6371.0088;

    public static IReadOnlyList<EventDraft> Build(IEnumerable<EventAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var drafts = new List<EventDraft>();
        var current = new List<EventAsset>();
        EventAsset? lastGeotagged = null;
        EventAsset? previous = null;

        // Ordinal ids, so the order never depends on the machine's culture.
        foreach (var asset in assets.OrderBy(a => a.CapturedAt).ThenBy(a => a.Id, StringComparer.Ordinal))
        {
            var hasLocation = asset.Latitude is not null && asset.Longitude is not null;
            var splits = previous is not null
                && (asset.CapturedAt - previous.CapturedAt > MaxGap
                    || (hasLocation && lastGeotagged is not null && DistanceKm(lastGeotagged, asset) > MaxDistanceKm));
            if (splits)
            {
                drafts.Add(Close(current));
                current = [];
                lastGeotagged = null;
            }

            current.Add(asset);
            previous = asset;
            if (hasLocation)
            {
                lastGeotagged = asset;
            }
        }

        if (current.Count > 0)
        {
            drafts.Add(Close(current));
        }

        return drafts;
    }

    /// <summary>The great-circle distance between two geotagged assets, in kilometres (haversine).</summary>
    public static double DistanceKm(EventAsset a, EventAsset b)
    {
        var (lat1, lon1) = (ToRadians(a.Latitude!.Value), ToRadians(a.Longitude!.Value));
        var (lat2, lon2) = (ToRadians(b.Latitude!.Value), ToRadians(b.Longitude!.Value));
        var h = Math.Pow(Math.Sin((lat2 - lat1) / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin((lon2 - lon1) / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static EventDraft Close(List<EventAsset> assets)
    {
        var (latitude, longitude) = Centroid(assets);
        return new EventDraft(assets[0].CapturedAt, assets[^1].CapturedAt, latitude, longitude, PlaceLabel(assets), [.. assets.Select(a => a.Id)]);
    }

    // The mean of the unit vectors, so a trip across the 180th meridian does not average to the other side of the Earth.
    private static (double? Latitude, double? Longitude) Centroid(List<EventAsset> assets)
    {
        double x = 0, y = 0, z = 0;
        var count = 0;
        foreach (var asset in assets.Where(a => a.Latitude is not null && a.Longitude is not null))
        {
            var (lat, lon) = (ToRadians(asset.Latitude!.Value), ToRadians(asset.Longitude!.Value));
            x += Math.Cos(lat) * Math.Cos(lon);
            y += Math.Cos(lat) * Math.Sin(lon);
            z += Math.Sin(lat);
            count++;
        }

        // Nothing geotagged, or points that cancel out exactly (opposite sides of the Earth): no centre.
        if (count == 0 || Math.Sqrt(x * x + y * y + z * z) < 1e-9 * count)
        {
            return (null, null);
        }

        return (Math.Round(ToDegrees(Math.Atan2(z, Math.Sqrt(x * x + y * y))), 6), Math.Round(ToDegrees(Math.Atan2(y, x)), 6));
    }

    // The commonest city, else the commonest country; ties go to the name that sorts first, so a rebuild never flips.
    private static string? PlaceLabel(List<EventAsset> assets) =>
        Commonest(assets.Select(a => a.City)) ?? Commonest(assets.Select(a => a.Country));

    private static string? Commonest(IEnumerable<string?> names) =>
        names.Where(n => !string.IsNullOrWhiteSpace(n))
            .GroupBy(n => n!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static double ToDegrees(double radians) => radians * 180 / Math.PI;
}
