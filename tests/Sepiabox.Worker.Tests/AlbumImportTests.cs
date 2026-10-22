using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Storage;
using Sepiabox.Worker.Importing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests;

/// <summary>The caption and albums of a manifest in the catalog (S-052, D-114): imported, idempotent, and only ever added to.</summary>
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
    public async Task Cuts_a_very_long_album_name_to_the_length_the_search_takes_instead_of_failing_the_manifest()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var longName = new string('a', 5000);
        await PutAsync(container, Photo(Source("Instagram", "media/a.jpg")) with { Albums = [longName, "Ferias"] });

        await ImportAsync(db, container);

        var names = (await db.AssetAlbums.AsNoTracking().Select(a => a.Name).ToListAsync()).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["Ferias", new string('a', 200)], names);
    }

    [Theory]
    [InlineData("  Ferias  ", "Ferias")]
    [InlineData("Fe\u0000rias", "Ferias")]
    [InlineData("   ", null)]
    [InlineData("Praia\u202E 2019", "Praia 2019")]
    [InlineData("Ver\u200Bão\uFEFF", "Verão")]
    [InlineData("Fam\u200D\u00EDlia", "Fam\u200D\u00EDlia")]
    public void Keeps_an_album_name_trimmed_and_without_control_characters(string name, string? expected)
    {
        Assert.Equal(expected, ManifestImporter.AlbumName(name));
    }

    [Fact]
    public void Does_not_cut_an_album_name_in_the_middle_of_a_character()
    {
        var name = new string('a', 199) + "\U0001F600" + "tail";

        var cut = ManifestImporter.AlbumName(name)!;

        Assert.Equal(new string('a', 199), cut);
        Assert.True(char.IsLetter(cut[^1]));
    }

    [Fact]
    public async Task Stores_where_the_date_came_from_so_the_page_can_say_it_is_the_exports()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = Photo(Source("Instagram", "media/a.jpg")) with
        {
            Captured = new CaptureDate(new DateTime(2015, 6, 1, 11, 0, 0), CaptureDateSource.Export),
        };
        await PutAsync(container, manifest);

        await ImportAsync(db, container);

        Assert.Equal("export", (await db.Assets.AsNoTracking().SingleAsync()).CapturedAtSource);
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
