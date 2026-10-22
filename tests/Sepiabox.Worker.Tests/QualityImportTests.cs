using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Storage;
using Sepiabox.Worker.Importing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests;

/// <summary>The import of a photo's sharpness and hash (S-027, D-098); setting a photo aside is the person's, never an import's.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class QualityImportTests(CatalogFixture fixture)
{
    private static readonly ManifestQuality Measured = new(123.45, "0123456789abcdef");

    [Fact]
    public async Task A_manifest_with_quality_puts_the_sharpness_and_hash_on_the_asset()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Manifest() with { Quality = Measured });

        var result = await ImportAsync(db, container);

        Assert.Empty(result.Problems);
        var asset = await db.Assets.AsNoTracking().SingleAsync();
        Assert.Equal((123.45, "0123456789abcdef", false), (asset.Sharpness, asset.DHash, asset.Hidden));
    }

    [Fact]
    public async Task A_manifest_without_quality_keeps_what_is_stored()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Manifest() with { Quality = Measured });
        await ImportAsync(db, container);

        await PutAsync(container, Manifest());
        await ImportAsync(db, container);

        var asset = await db.Assets.AsNoTracking().SingleAsync();
        Assert.Equal((123.45, "0123456789abcdef"), (asset.Sharpness, asset.DHash));
    }

    [Fact]
    public async Task A_new_measure_replaces_the_stored_one_and_an_import_never_brings_back_a_hidden_photo()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Manifest() with { Quality = Measured });
        await ImportAsync(db, container);
        await db.Assets.ExecuteUpdateAsync(set => set.SetProperty(a => a.Hidden, true));

        await PutAsync(container, Manifest() with { Quality = new ManifestQuality(500, "ffffffffffffffff") });
        await ImportAsync(db, container);

        var asset = await db.Assets.AsNoTracking().SingleAsync();
        Assert.Equal((500.0, "ffffffffffffffff", true), (asset.Sharpness, asset.DHash, asset.Hidden));
    }

    private static Manifest Manifest() => Photo(Source("HD da Ana", "2019/praia.jpg"));

    private static Task<ImportResult> ImportAsync(CatalogDbContext db, BlobContainerClient container) =>
        new ManifestImporter(db, TestBlobStore.Of(container), new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(container.Name, CancellationToken.None);

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
