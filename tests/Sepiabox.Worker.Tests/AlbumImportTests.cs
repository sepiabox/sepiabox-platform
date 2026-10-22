using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Storage;
using Sepiabox.Worker.Importing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests;

/// <summary>The caption and albums of a manifest in the catalog (S-052, D-112): imported, idempotent, and only ever added to.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class AlbumImportTests(CatalogFixture fixture)
{
    [Fact]
    public async Task Imports_the_caption_and_both_albums_and_a_second_import_of_the_same_manifest_adds_no_duplicate()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = Photo(Source("Instagram", "media/a.jpg")) with { Caption = "Praia de Matosinhos", Albums = ["Ferias", "Viagem"] };
        await PutAsync(container, manifest);

        await ImportAsync(db, container);
        await PutAsync(container, manifest with { Sources = [Source("Instagram", "media/a.jpg", hour: 18)] });
        await ImportAsync(db, container);

        var asset = await db.Assets.AsNoTracking().Include(a => a.Albums).SingleAsync();
        Assert.Equal("Praia de Matosinhos", asset.Caption);
        Assert.Equal(["Ferias", "Viagem"], asset.Albums.Select(a => a.Name).Order());
    }

    [Fact]
    public async Task A_later_manifest_with_a_third_album_gives_the_asset_three_and_one_without_albums_leaves_them()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var first = Photo(Source("Instagram", "media/a.jpg")) with { Caption = "Praia", Albums = ["Ferias", "Viagem"] };
        await PutAsync(container, first);
        await ImportAsync(db, container);

        await PutAsync(container, first with { Albums = ["Ferias", "Verão 2019", "Viagem"] });
        await ImportAsync(db, container);
        await PutAsync(container, first with { Caption = null, Albums = null, Sources = [Source("HD da Ana", "2019/praia.jpg")] });
        await ImportAsync(db, container);

        var asset = await db.Assets.AsNoTracking().Include(a => a.Albums).SingleAsync();
        Assert.Equal(["Ferias", "Verão 2019", "Viagem"], asset.Albums.Select(a => a.Name).Order(StringComparer.Ordinal));
        // A manifest with no caption leaves the stored one.
        Assert.Equal("Praia", asset.Caption);
    }

    [Fact]
    public async Task Deleting_an_asset_takes_its_album_memberships_with_it()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("Instagram", "media/a.jpg")) with { Albums = ["Ferias"] });
        await ImportAsync(db, container);

        await db.Assets.ExecuteDeleteAsync();

        Assert.Equal(0, await db.AssetAlbums.CountAsync());
    }

    private static async Task ImportAsync(CatalogDbContext db, BlobContainerClient container)
    {
        var result = await new ManifestImporter(db, TestBlobStore.Of(container), new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(container.Name, CancellationToken.None);
        Assert.Empty(result.Problems);
    }

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
