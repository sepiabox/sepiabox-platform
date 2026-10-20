using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Places;

/// <summary>
/// <c>places refresh</c> (S-012): names the place of every asset that has a location but no place, such as
/// those imported before GeoNames was loaded. An asset with no city within 50 km stays without one. With
/// <c>all</c> (<c>places refresh --all</c>), every asset with a location is named again, after a newer dump.
/// </summary>
public sealed class PlacesRefresh(CatalogDbContext db, IPlaceResolver places, bool all = false)
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
                .Where(a => a.Location != null && (all || a.PlaceCity == null) && (after == null || string.Compare(a.Id, after) > 0))
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return named;
            }

            foreach (var asset in batch)
            {
                var place = await places.ResolveAsync(asset.Location!, cancellationToken);
                if (place is not null || all)
                {
                    ManifestImporter.SetPlace(asset, place);
                }

                if (place is not null)
                {
                    named++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            after = batch[^1].Id;
        }
    }
}
