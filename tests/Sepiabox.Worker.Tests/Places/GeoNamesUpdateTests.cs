using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Worker.Places;

namespace Sepiabox.Worker.Tests.Places;

/// <summary><c>geonames update</c> (S-059) against a fake GeoNames server and a real PostgreSQL.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class GeoNamesUpdateTests(CatalogFixture fixture)
{
    // The sample has 5 cities; the guard's floor scaled down to it.
    private static readonly GeoNamesLoadGuard s_guard = new(4, 0.10);

    [Fact]
    public async Task Downloads_loads_and_names_assets_without_a_place()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        db.Assets.Add(AssetAt(new string('a', 64), 41.18, -8.69));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await UpdateAsync(db, FakeGeoNames.WithSample());

        Assert.Equal(new GeoNamesUpdate.Result(5, 1), result);
        Assert.Equal(5, await db.GeoNames.CountAsync());
        Assert.Equal("Matosinhos", (await db.Assets.SingleAsync()).PlaceCity);
    }

    [Theory]
    [InlineData(3)] // Below the floor.
    [InlineData(4)] // 20% fewer than the 5 loaded.
    public async Task A_refused_download_keeps_the_cities_loaded(int cities)
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await UpdateAsync(db, FakeGeoNames.WithSample());

        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateAsync(db, FakeGeoNames.WithSample(cities)));

        Assert.Equal(5, await db.GeoNames.CountAsync());
    }

    [Fact]
    public async Task A_failed_download_keeps_the_cities_loaded_and_leaves_no_files()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await UpdateAsync(db, FakeGeoNames.WithSample());
        var broken = FakeGeoNames.WithSample();
        broken.Files.Remove("countryInfo.txt");
        var before = Directory.EnumerateDirectories(Path.GetTempPath(), "sepiabox-geonames-*").Count();

        await Assert.ThrowsAsync<HttpRequestException>(() => UpdateAsync(db, broken));

        Assert.Equal(5, await db.GeoNames.CountAsync());
        Assert.Equal(before, Directory.EnumerateDirectories(Path.GetTempPath(), "sepiabox-geonames-*").Count());
    }

    private static async Task<GeoNamesUpdate.Result> UpdateAsync(CatalogDbContext db, FakeGeoNames geoNames)
    {
        using var http = new HttpClient(geoNames);
        var logger = NullLogger.Instance;
        return await new GeoNamesUpdate(db, new GeoNamesDownloader(http, logger), new PostgisPlaceResolver(db), s_guard, logger)
            .RunAsync(CancellationToken.None);
    }

    private static Asset AssetAt(string id, double latitude, double longitude) => new()
    {
        Id = id,
        Kind = "photo",
        ContentType = "image/jpeg",
        SizeBytes = 1,
        Location = new NetTopologySuite.Geometries.Point(longitude, latitude) { SRID = 4326 },
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
