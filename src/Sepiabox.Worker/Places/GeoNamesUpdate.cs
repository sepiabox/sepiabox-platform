using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;

namespace Sepiabox.Worker.Places;

/// <summary>
/// <c>geonames update</c>, what the <c>sepiabox-geonames</c> job runs (S-059, D-067): downloads the current
/// GeoNames files into a temporary folder, loads them when <see cref="GeoNamesLoadGuard"/> accepts them (the
/// load replaces the cities in one transaction, so any failure keeps the previous ones), then names the
/// assets that have no place yet.
/// </summary>
public sealed class GeoNamesUpdate(
    CatalogDbContext db, GeoNamesDownloader downloader, IPlaceResolver places, GeoNamesLoadGuard guard, ILogger logger)
{
    public sealed record Result(int Cities, int Named);

    public async Task<Result> RunAsync(CancellationToken cancellationToken)
    {
        var folder = Directory.CreateTempSubdirectory("sepiabox-geonames-").FullName;
        try
        {
            var source = await downloader.DownloadAsync(folder, cancellationToken);
            var admin1 = GeoNamesFiles.ReadAdmin1(source.Admin1);
            var countries = GeoNamesFiles.ReadCountries(source.Countries);
            // Read whole (about 200,000 small records) so they are counted before the table is touched.
            var cities = GeoNamesFiles.ReadCities(source.Cities).ToList();
            guard.Check(cities.Count, await db.GeoNames.CountAsync(cancellationToken));

            var loaded = await new GeoNamesLoader(db).LoadAsync(cities, admin1, countries, cancellationToken);
            Log.GeoNamesLoaded(logger, loaded);
            var named = await new PlacesRefresh(db, places).RunAsync(cancellationToken);
            Log.PlacesRefreshed(logger, named);
            return new Result(loaded, named);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The replica's storage goes with it; what failed before matters more than a leftover folder.
                Log.TemporaryFolderLeft(logger, folder, ex);
            }
        }
    }
}
