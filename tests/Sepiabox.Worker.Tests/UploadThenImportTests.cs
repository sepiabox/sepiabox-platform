using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Uploading;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Tests;

[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class UploadThenImportTests(CatalogFixture fixture)
{
    [Fact]
    public async Task Two_work_folders_uploaded_before_one_import_keep_both_sources_and_all_derivatives()
    {
        using var anaDrive = new TempFolder();
        using var anaWork = new TempFolder();
        using var jonathasDrive = new TempFolder();
        using var jonathasWork = new TempFolder();
        anaDrive.Write("2019/praia.jpg", TestPhotos.Porto("red"));
        jonathasDrive.Write("Fotos/IMG_0001.jpg", TestPhotos.Porto("red"));
        ProcessCommand.Run(new ProcessOptions(anaDrive.Path, anaWork.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None));
        ProcessCommand.Run(new ProcessOptions(jonathasDrive.Path, jonathasWork.Path, "HD do Jonathas", FfmpegFolder: TestFfmpeg.None));
        await UploadCommand.RunAsync(new UploadOptions(anaWork.Path, UploadDestination.Local), fixture.Blobs);
        await UploadCommand.RunAsync(new UploadOptions(jonathasWork.Path, UploadDestination.Local), fixture.Blobs);
        await using var db = await fixture.CreateMigratedDatabaseAsync();

        var result = await new ManifestImporter(db, new ManifestValidator(), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(fixture.Blobs.GetBlobContainerClient(BlobLayout.ManifestsContainer), CancellationToken.None);

        Assert.Equal(1, result.Imported);
        var asset = await db.Assets.AsNoTracking().Include(a => a.Sources).Include(a => a.Derivatives).SingleAsync();
        Assert.Equal(["HD da Ana", "HD do Jonathas"], asset.Sources.Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["preview-1280", "thumb-256"], asset.Derivatives.Select(d => d.Name).Order(StringComparer.Ordinal));
    }
}
