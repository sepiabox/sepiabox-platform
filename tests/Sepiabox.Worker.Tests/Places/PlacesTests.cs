using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Storage;
using Sepiabox.Worker.Importing;
using Sepiabox.Worker.Places;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests.Places;

/// <summary>GeoNames load, place resolution on import, the 50 km radius and <c>places refresh</c> (S-012).</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class PlacesTests(CatalogFixture fixture) : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Loading_the_same_five_cities_twice_leaves_five_rows()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var source = GeoNamesSample.Write(_folder);

        Assert.Equal(5, await LoadAsync(db, source));
        Assert.Equal(5, await LoadAsync(db, source));

        Assert.Equal(5, await db.GeoNames.CountAsync());
        var matosinhos = await db.GeoNames.AsNoTracking().SingleAsync(g => g.GeonameId == 1002);
        Assert.Equal(("Matosinhos", "PT", "Portugal", "Porto", 45_703L), (matosinhos.Name, matosinhos.CountryCode, matosinhos.CountryName, matosinhos.Admin1Name, matosinhos.Population));
        Assert.Equal((-8.68908, 41.18207, 4326), (matosinhos.Location.X, matosinhos.Location.Y, matosinhos.Location.SRID));
    }

    [Fact]
    public async Task Loading_a_newer_dump_replaces_the_cities()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await LoadAsync(db, GeoNamesSample.Write(_folder));
        await LoadAsync(db, GeoNamesSample.Write(_folder, count: 3));

        Assert.Equal(["Lisbon", "Matosinhos", "Porto"], await db.GeoNames.Select(g => g.Name).OrderBy(n => n).ToListAsync());

        var zipped = GeoNamesSample.Write(_folder) with { Cities = GeoNamesSample.WriteZip(_folder) };
        Assert.Equal(5, await LoadAsync(db, zipped));
        Assert.Equal(5, await db.GeoNames.CountAsync());
    }

    [Fact]
    public async Task A_failed_load_keeps_the_cities_already_loaded()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await LoadAsync(db, GeoNamesSample.Write(_folder));
        var broken = _folder.Write("broken.txt", System.Text.Encoding.UTF8.GetBytes(GeoNamesSample.CitiesText(2) + "not a city\n"));

        await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(db, GeoNamesSample.Write(_folder) with { Cities = broken }));

        db.ChangeTracker.Clear();
        Assert.Equal(5, await db.GeoNames.CountAsync());
    }

    [Fact]
    public async Task An_asset_imported_at_41_18_minus_8_69_is_in_Matosinhos_Porto_Portugal()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await LoadAsync(db, GeoNamesSample.Write(_folder));
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, PhotoAt(PhotoId, 41.18, -8.69));

        await ImportAsync(db, container);

        Assert.Equal(("Matosinhos", "Porto", "Portugal"), await PlaceAsync(db, PhotoId));
    }

    [Theory]
    [InlineData(41.18, -12.4)] // At sea, about 300 km west of Matosinhos.
    [InlineData(38.24, -25.66874)] // 55 km north of Ponta Delgada, the only city on its island.
    public async Task An_asset_farther_than_50_km_from_every_city_has_no_place(double latitude, double longitude)
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await LoadAsync(db, GeoNamesSample.Write(_folder));
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, PhotoAt(PhotoId, latitude, longitude));

        await ImportAsync(db, container);

        Assert.Equal((null, null, null), await PlaceAsync(db, PhotoId));
    }

    [Fact]
    public async Task An_asset_within_50_km_of_a_city_is_named_after_it()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await LoadAsync(db, GeoNamesSample.Write(_folder));

        // 44 km north of Ponta Delgada.
        var place = await new PostgisPlaceResolver(db).ResolveAsync(Point(38.14, -25.66874), CancellationToken.None);

        Assert.Equal(new Place("Ponta Delgada", "Azores", "Portugal"), place);
    }

    [Fact]
    public async Task A_new_location_renames_the_place_and_no_location_clears_it()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await LoadAsync(db, GeoNamesSample.Write(_folder));
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, PhotoAt(PhotoId, 41.18, -8.69));
        await ImportAsync(db, container);

        await PutAsync(container, PhotoAt(PhotoId, 38.72, -9.14));
        await ImportAsync(db, container);
        Assert.Equal(("Lisbon", "Lisbon", "Portugal"), await PlaceAsync(db, PhotoId));

        await PutAsync(container, PhotoAt(PhotoId, 38.72, -9.14) with { Location = null });
        await ImportAsync(db, container);
        Assert.Equal((null, null, null), await PlaceAsync(db, PhotoId));
    }

    [Fact]
    public async Task Places_refresh_names_assets_imported_before_GeoNames_was_loaded()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var matosinhos = new string('a', 64);
        var madrid = new string('b', 64);
        var atSea = new string('c', 64);
        await PutAsync(container, PhotoAt(matosinhos, 41.18, -8.69));
        await PutAsync(container, PhotoAt(madrid, 40.42, -3.70));
        await PutAsync(container, PhotoAt(atSea, 41.18, -12.4));
        await ImportAsync(db, container);
        Assert.Equal((null, null, null), await PlaceAsync(db, matosinhos));

        await LoadAsync(db, GeoNamesSample.Write(_folder));
        var named = await new PlacesRefresh(db, new PostgisPlaceResolver(db)).RunAsync(CancellationToken.None);

        Assert.Equal(2, named);
        Assert.Equal(("Matosinhos", "Porto", "Portugal"), await PlaceAsync(db, matosinhos));
        Assert.Equal(("Madrid", "Madrid", "Spain"), await PlaceAsync(db, madrid));
        Assert.Equal((null, null, null), await PlaceAsync(db, atSea));
        Assert.Equal(0, await new PlacesRefresh(db, new PostgisPlaceResolver(db)).RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_newer_dump_renames_places_only_with_refresh_all()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await LoadAsync(db, GeoNamesSample.Write(_folder));
        var container = await fixture.CreateContainerAsync();
        var madrid = new string('b', 64);
        await PutAsync(container, PhotoAt(PhotoId, 41.18, -8.69));
        await PutAsync(container, PhotoAt(madrid, 40.42, -3.70));
        await ImportAsync(db, container);
        // The newer dump has no Madrid and calls Matosinhos by another name.
        var renamed = GeoNamesSample.CitiesText(4).Replace("\tMatosinhos\tMatosinhos\t", "\tMatosinhos (Porto)\tMatosinhos\t", StringComparison.Ordinal);
        await LoadAsync(db, GeoNamesSample.Write(_folder) with { Cities = _folder.Write("newer.txt", System.Text.Encoding.UTF8.GetBytes(renamed)) });

        Assert.Equal(0, await new PlacesRefresh(db, new PostgisPlaceResolver(db)).RunAsync(CancellationToken.None));
        Assert.Equal(("Matosinhos", "Porto", "Portugal"), await PlaceAsync(db, PhotoId));

        Assert.Equal(1, await new PlacesRefresh(db, new PostgisPlaceResolver(db), all: true).RunAsync(CancellationToken.None));
        Assert.Equal(("Matosinhos (Porto)", "Porto", "Portugal"), await PlaceAsync(db, PhotoId));
        Assert.Equal((null, null, null), await PlaceAsync(db, madrid));
    }

    [Fact]
    public async Task Places_refresh_never_overwrites_the_place_of_an_asset_moved_meanwhile()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, PhotoAt(PhotoId, 41.18, -8.69));
        await ImportAsync(db, container);
        await LoadAsync(db, GeoNamesSample.Write(_folder));
        // An import moves the asset to Lisbon, and names it, while refresh resolves the old location.
        var moving = new MovingResolver(new PostgisPlaceResolver(db), () => db.Database.ExecuteSqlRawAsync($$"""
            UPDATE assets SET location = 'SRID=4326;POINT(-9.14 38.72)'::geography,
              place_city = 'Lisbon', place_region = 'Lisbon', place_country = 'Portugal'
            WHERE id = '{{PhotoId}}'
            """));

        var named = await new PlacesRefresh(db, moving, all: true).RunAsync(CancellationToken.None);

        Assert.Equal(0, named);
        Assert.Equal(("Lisbon", "Lisbon", "Portugal"), await PlaceAsync(db, PhotoId));
    }

    private sealed class MovingResolver(IPlaceResolver inner, Func<Task> move) : IPlaceResolver
    {
        public async Task<Place?> ResolveAsync(NetTopologySuite.Geometries.Point location, CancellationToken cancellationToken)
        {
            var place = await inner.ResolveAsync(location, cancellationToken);
            await move();
            return place;
        }
    }

    [Fact]
    public async Task Every_position_of_the_fixture_library_resolves_to_its_city_with_the_sample()
    {
        // S-054 counts the fixture library's assets per city with this sample (S-061).
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await LoadAsync(db, GeoNamesSample.Write(_folder));
        var resolver = new PostgisPlaceResolver(db);

        foreach (var (city, at) in FixtureLibrary.CityPoints)
        {
            var place = await resolver.ResolveAsync(Point(at.Latitude, at.Longitude), CancellationToken.None);
            Assert.True(place?.City == city, $"{at} resolved to {place?.City ?? "no city"}, not {city}.");
        }
    }

    private static NetTopologySuite.Geometries.Point Point(double latitude, double longitude) =>
        new(longitude, latitude) { SRID = 4326 };

    private static Manifest PhotoAt(string id, double latitude, double longitude) =>
        Photo(Source("HD da Ana", $"{id[..4]}.jpg")) with { Id = id, Location = new GeoPoint(latitude, longitude) };

    private static Task<int> LoadAsync(CatalogDbContext db, GeoNamesSource source) =>
        new GeoNamesLoader(db).LoadAsync(
            GeoNamesFiles.ReadCities(source.Cities),
            GeoNamesFiles.ReadAdmin1(source.Admin1),
            GeoNamesFiles.ReadCountries(source.Countries),
            CancellationToken.None);

    private static async Task ImportAsync(CatalogDbContext db, BlobContainerClient container)
    {
        var result = await new ManifestImporter(
                db, TestBlobStore.Of(container), new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(container.Name, CancellationToken.None);
        Assert.Empty(result.Problems);
    }

    private static async Task<(string?, string?, string?)> PlaceAsync(CatalogDbContext db, string id)
    {
        var asset = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == id);
        return (asset.PlaceCity, asset.PlaceRegion, asset.PlaceCountry);
    }

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
