using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sepiabox.Catalog;

namespace Sepiabox.Worker.Places;

/// <summary>
/// <c>geonames load</c> (S-012): replaces the <c>geonames</c> table with a dump's cities, in one transaction,
/// so loading the same files again leaves the same rows and a failed load leaves the old ones. Plain EF Core
/// (D-051). <c>DELETE</c>, not <c>TRUNCATE</c>: TRUNCATE would lock the table against readers for the whole load,
/// so imports looking up places would time out; with DELETE they keep reading the old cities until the commit
/// (D-066). The dead rows are then vacuumed, which needs the table's owner, the administrator that runs the load.
/// </summary>
public sealed class GeoNamesLoader(CatalogDbContext db)
{
    private const int BatchSize = 5000;
    private static readonly GeometryFactory s_wgs84 = new(new PrecisionModel(), 4326);

    public async Task<int> LoadAsync(
        IEnumerable<GeoNamesCity> cities,
        IReadOnlyDictionary<string, string> admin1,
        IReadOnlyDictionary<string, string> countries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cities);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.GeoNames.ExecuteDeleteAsync(cancellationToken);

        var loaded = 0;
        foreach (var batch in cities.Chunk(BatchSize))
        {
            db.GeoNames.AddRange(batch.Select(city => new GeoName
            {
                GeonameId = city.GeonameId,
                Name = city.Name,
                AsciiName = city.AsciiName,
                CountryCode = city.CountryCode,
                CountryName = countries.GetValueOrDefault(city.CountryCode),
                Admin1Name = admin1.GetValueOrDefault($"{city.CountryCode}.{city.Admin1Code}"),
                Population = city.Population,
                Location = s_wgs84.CreatePoint(new Coordinate(city.Longitude, city.Latitude)),
            }));
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            loaded += batch.Length;
        }

        await transaction.CommitAsync(cancellationToken);

        // Outside the transaction, as VACUUM requires; autovacuum would get there too, this just does it now.
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) geonames", cancellationToken);
        return loaded;
    }
}
