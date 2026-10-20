using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Places;

/// <summary>
/// <c>places refresh</c> (S-012): names the place of every asset that has a location but no place, such as
/// those imported before GeoNames was loaded. An asset with no city within 50 km stays without one.
/// </summary>
public sealed class PlacesRefresh(CatalogDbContext db, IPlaceResolver places)
{
    private const int BatchSize = 500;

    /// <summary>How many assets now have a place.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var named = 0;
        string? after = null;
        while (true)
        {
            // By id, so assets left without a place (none within 50 km) are not read again in this run.
            var batch = await db.Assets
                .Where(a => a.Location != null && a.PlaceCity == null && (after == null || string.Compare(a.Id, after) > 0))
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return named;
            }

            foreach (var asset in batch)
            {
                if (await places.ResolveAsync(asset.Location!, cancellationToken) is { } place)
                {
                    ManifestImporter.SetPlace(asset, place);
                    named++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            after = batch[^1].Id;
        }
    }
}
