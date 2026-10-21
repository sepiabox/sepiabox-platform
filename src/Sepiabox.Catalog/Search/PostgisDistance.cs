namespace Sepiabox.Catalog.Search;

/// <summary>
/// The family's places in a search (S-014, D-083), the PostGIS way (D-051): NetTopologySuite's
/// <c>IsWithinDistance</c>, which Npgsql turns into <c>ST_DWithin</c> on the geography columns, so distances are metres
/// on the spheroid. Assets without a location never match.
/// </summary>
public static class PostgisDistance
{
    /// <summary>Assets taken inside any of <paramref name="places"/>: within each place's own radius of its centre.</summary>
    public static IQueryable<Asset> InAny(IQueryable<Asset> assets, IQueryable<SavedPlace> places) =>
        assets.Where(a => a.Location != null && places.Any(p => a.Location.IsWithinDistance(p.Location, p.RadiusM)));

    /// <summary>
    /// Assets taken more than <paramref name="minMeters"/> and at most <paramref name="maxMeters"/> from
    /// <paramref name="place"/>'s centre (each bound when given).
    /// </summary>
    public static IQueryable<Asset> Between(IQueryable<Asset> assets, IQueryable<SavedPlace> place, double? minMeters, double? maxMeters)
    {
        assets = assets.Where(a => a.Location != null && place.Any());
        if (minMeters is { } min)
        {
            assets = assets.Where(a => place.Any(p => !a.Location!.IsWithinDistance(p.Location, min)));
        }

        if (maxMeters is { } max)
        {
            assets = assets.Where(a => place.Any(p => a.Location!.IsWithinDistance(p.Location, max)));
        }

        return assets;
    }
}
