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

/// <summary>A manifest from <c>process --export</c> imports: its caption and albums wait for S-052, its date source is a date source (S-045).</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ExportFieldsImportTests(CatalogFixture fixture)
{
    [Fact]
    public async Task Imports_a_manifest_with_an_export_date_a_caption_and_albums_and_a_manifest_without_them()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var plain = Photo(Source("HD da Ana", "2019/praia.jpg"));
        var exported = plain with
        {
            Id = new string('b', 64),
            Captured = new CaptureDate(new DateTime(2015, 6, 1, 11, 0, 0), CaptureDateSource.Export),
            Caption = "Praia de Matosinhos",
            Albums = ["Ferias", "Viagem"],
            Sources = [Source("Instagram", "media/a.jpg")],
            Derivatives = [],
        };
        await PutAsync(container, plain);
        await PutAsync(container, exported);

        var result = await new ManifestImporter(db, TestBlobStore.Of(container), new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(container.Name, CancellationToken.None);

        Assert.Empty(result.Problems);
        Assert.Equal(2, await db.Assets.CountAsync());
        Assert.Equal(new DateTime(2015, 6, 1, 11, 0, 0), (await db.Assets.AsNoTracking().SingleAsync(a => a.Id == exported.Id)).CapturedAt);
    }

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
