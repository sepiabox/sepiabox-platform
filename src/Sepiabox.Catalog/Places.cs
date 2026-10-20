using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Sepiabox.Catalog;

/// <summary>A city, its first-level region and its country, as GeoNames names them.</summary>
public sealed record Place(string City, string? Region, string? Country);

/// <summary>
/// Names the place of a location (S-012, D-051): the nearest GeoNames city within
/// <see cref="PlaceSearch.MaxDistanceMeters"/>, or none.
/// </summary>
public interface IPlaceResolver
{
    Task<Place?> ResolveAsync(Point location, CancellationToken cancellationToken);
}

public static class PlaceSearch
{
    /// <summary>A city farther than this names nothing: a photo at sea or in the wild has no place (S-012).</summary>
    public const double MaxDistanceMeters = 50_000;
}

/// <summary>
/// The PostGIS implementation (D-051): the nearest city by the GiST index's <c>&lt;-&gt;</c> distance order,
/// limited to <see cref="PlaceSearch.MaxDistanceMeters"/> with <c>ST_DWithin</c>, both on geography (metres).
/// </summary>
public sealed class PostgisPlaceResolver(CatalogDbContext db) : IPlaceResolver
{
    public async Task<Place?> ResolveAsync(Point location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var longitude = location.X;
        var latitude = location.Y;
        var nearest = await db.GeoNames
            .FromSql($"""
                SELECT * FROM geonames
                WHERE ST_DWithin(location, ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326)::geography, {PlaceSearch.MaxDistanceMeters})
                ORDER BY location <-> ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326)::geography
                LIMIT 1
                """)
            .AsNoTracking()
            .Select(g => new { g.Name, g.Admin1Name, g.CountryName })
            .FirstOrDefaultAsync(cancellationToken);

        return nearest is null ? null : new Place(nearest.Name, nearest.Admin1Name, nearest.CountryName);
    }
}
