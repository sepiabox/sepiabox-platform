using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Importing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests;

/// <summary>How the import handles index-only manifests (S-063, D-088): where the original lives and whether it is stored.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class IndexOnlyImportTests(CatalogFixture fixture)
{
    private const string Url = "https://photos.google.com/photo/AF1Qip-example";

    [Fact]
    public async Task Imports_an_index_only_manifest_with_its_source_url()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var source = Source("Takeout 2026", "Google Photos/IMG_0001.jpg") with { Url = Url };
        await PutAsync(container, Photo(source) with { OriginalStored = false });

        var result = await ImportAsync(db, container);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Problems);
        Assert.False((await db.Assets.AsNoTracking().SingleAsync()).OriginalStored);
        Assert.Equal(Url, (await db.AssetSources.AsNoTracking().SingleAsync()).Url);
    }

    [Fact]
    public async Task Keeps_a_source_url_when_a_later_manifest_has_none()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var withUrl = Photo(Source("Takeout 2026", "Google Photos/IMG_0001.jpg") with { Url = Url }) with { OriginalStored = false };
        await PutAsync(container, withUrl);
        await ImportAsync(db, container);

        await PutAsync(container, Photo(Source("Takeout 2026", "Google Photos/IMG_0001.jpg", hour: 18)) with { OriginalStored = false });
        await ImportAsync(db, container);

        var source = await db.AssetSources.AsNoTracking().SingleAsync();
        Assert.Equal(Url, source.Url);
        Assert.Equal(new DateTimeOffset(2019, 7, 14, 17, 0, 0, TimeSpan.Zero), source.ModifiedAt);
    }

    [Fact]
    public async Task Imports_a_manifest_without_the_new_fields_as_stored_with_no_url()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = Photo(Source("HD da Ana", "2019/praia.jpg"));
        var json = JsonNode.Parse(ManifestJson.Serialize(manifest))!.AsObject();
        Assert.False(json.ContainsKey("originalStored"));
        Assert.False(json["sources"]![0]!.AsObject().ContainsKey("url"));
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(json.ToJsonString()), overwrite: true);

        var result = await ImportAsync(db, container);

        Assert.Equal(1, result.Imported);
        Assert.True((await db.Assets.AsNoTracking().SingleAsync()).OriginalStored);
        Assert.Null((await db.AssetSources.AsNoTracking().SingleAsync()).Url);
    }

    [Fact]
    public async Task An_asset_stored_in_full_mode_stays_stored_when_an_index_only_manifest_arrives_after_it()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var full = Photo(Source("HD da Ana", "2019/praia.jpg"));
        var indexOnly = Photo(Source("Takeout 2026", "Google Photos/IMG_0001.jpg") with { Url = Url }) with { OriginalStored = false };
        await PutAsync(container, full);
        await ImportAsync(db, container);

        // As upload writes it (D-036): the merge of both, and, separately, the index-only manifest on its own, which a
        // worker that missed the merge would see.
        await PutAsync(container, indexOnly);
        await ImportAsync(db, container);
        Assert.True((await db.Assets.AsNoTracking().SingleAsync()).OriginalStored);
        await PutAsync(container, ManifestMerge.Merge(full, indexOnly));
        await ImportAsync(db, container);

        var asset = await db.Assets.AsNoTracking().Include(a => a.Sources).SingleAsync();
        Assert.True(asset.OriginalStored);
        Assert.Equal(["HD da Ana", "Takeout 2026"], asset.Sources.Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Url, asset.Sources.Single(s => s.Name == "Takeout 2026").Url);
    }

    [Fact]
    public async Task An_index_only_asset_becomes_stored_when_a_full_mode_manifest_arrives_after_it()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var full = Photo(Source("HD da Ana", "2019/praia.jpg"));
        var indexOnly = Photo(Source("Takeout 2026", "Google Photos/IMG_0001.jpg") with { Url = Url }) with { OriginalStored = false };
        await PutAsync(container, indexOnly);
        await ImportAsync(db, container);
        Assert.False((await db.Assets.AsNoTracking().SingleAsync()).OriginalStored);

        await PutAsync(container, ManifestMerge.Merge(indexOnly, full));
        await ImportAsync(db, container);

        var asset = await db.Assets.AsNoTracking().Include(a => a.Sources).SingleAsync();
        Assert.True(asset.OriginalStored);
        Assert.Equal(2, asset.Sources.Count);
        Assert.Equal(Url, asset.Sources.Single(s => s.Name == "Takeout 2026").Url);
    }

    [Fact]
    public async Task A_source_url_that_is_not_https_is_a_problem_for_that_manifest_only()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("Takeout 2026", "a.jpg") with { Url = "http://photos.example.com/a" }));

        var result = await ImportAsync(db, container);

        Assert.Equal(0, result.Imported);
        Assert.Contains("url", Assert.Single(result.Problems).Reason, StringComparison.Ordinal);
        Assert.Equal(0, await db.Assets.CountAsync());
    }

    [Fact]
    public async Task Existing_assets_are_stored_after_the_migration()
    {
        // The column's default is true, so every asset imported before index-only mode keeps meaning "stored".
        await using var db = await fixture.CreateEmptyDatabaseAsync();
        var migrations = db.Database.GetMigrations().ToList();
        var before = migrations[migrations.IndexOf(migrations.Single(m => m.EndsWith("_OriginalStoredAndSourceUrl", StringComparison.Ordinal))) - 1];
        await db.GetService<IMigrator>().MigrateAsync(before);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO assets (id, kind, size_bytes, content_type, imported_at, updated_at) VALUES ({0}, 'photo', 1, 'image/jpeg', now(), now())",
            PhotoId);

        await db.Database.MigrateAsync();

        Assert.True((await db.Assets.AsNoTracking().SingleAsync()).OriginalStored);
    }

    private static Task<ImportResult> ImportAsync(CatalogDbContext db, BlobContainerClient container) =>
        new ManifestImporter(db, new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(container, CancellationToken.None);

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
