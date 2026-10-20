using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Importing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests;

[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class EmbeddingImportTests(CatalogFixture fixture)
{
    [Fact]
    public async Task A_manifest_with_an_embedding_puts_it_in_asset_embeddings()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var vector = UnitVector(seed: 1);
        await PutAsync(container, WithClip(vector));

        var result = await ImportAsync(db, container);

        Assert.Empty(result.Problems);
        Assert.Empty(result.Warnings);
        var row = await db.AssetEmbeddings.SingleAsync();
        Assert.Equal((PhotoId, ClipImageEmbedder.ModelKey), (row.AssetId, row.Model));
        Assert.Equal(vector, row.Embedding.ToArray());
    }

    [Fact]
    public async Task A_new_embedding_for_the_same_model_replaces_the_stored_one()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, WithClip(UnitVector(seed: 1)));
        await ImportAsync(db, container);
        var replacement = UnitVector(seed: 2);

        await PutAsync(container, WithClip(replacement));
        await ImportAsync(db, container);

        var row = await db.AssetEmbeddings.SingleAsync();
        Assert.Equal(replacement, row.Embedding.ToArray());
    }

    [Fact]
    public async Task A_manifest_without_embeddings_keeps_the_stored_ones()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, WithClip(UnitVector(seed: 1)));
        await ImportAsync(db, container);

        await PutAsync(container, Photo(Source("HD da Ana", "2019/praia.jpg")));
        await ImportAsync(db, container);

        Assert.Equal(1, await db.AssetEmbeddings.CountAsync());
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    public async Task Imports_the_asset_with_a_warning_when_its_embedding_is_unusable(float value)
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var vector = value == 0f ? new float[ClipImageEmbedder.Dimensions] : UnitVector(seed: 1);
        vector[7] = value;
        await PutAsync(container, WithClip(vector));

        var result = await ImportAsync(db, container);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Problems);
        Assert.Contains("clip-vit-b32", Assert.Single(result.Warnings).Reason, StringComparison.Ordinal);
        var asset = await db.Assets.Include(a => a.Sources).Include(a => a.Derivatives).SingleAsync();
        Assert.Single(asset.Sources);
        Assert.Equal(2, asset.Derivatives.Count);
        Assert.Equal(0, await db.AssetEmbeddings.CountAsync());
    }

    [Fact]
    public async Task Ignores_the_embedding_when_the_catalog_does_not_store_its_model()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = WithClip(UnitVector(seed: 1));
        await PutAsync(container, manifest with
        {
            Embeddings = new Dictionary<string, JsonNode>(manifest.Embeddings) { ["other-model"] = EmbeddingCodec.Encode([1f, 2f]) },
        });

        var result = await ImportAsync(db, container);

        Assert.Empty(result.Problems);
        Assert.Equal(ClipImageEmbedder.ModelKey, (await db.AssetEmbeddings.SingleAsync()).Model);
    }

    [Fact]
    public async Task The_embedding_index_is_hnsw_with_cosine_distance()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var indexDefinition = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_asset_embeddings_embedding'")
            .SingleAsync();

        Assert.Contains("USING hnsw", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("vector_cosine_ops", indexDefinition, StringComparison.Ordinal);
    }

    // Pgvector.EntityFrameworkCore 0.3.0 is built against EF Core 9; this checks its translation on EF Core 10.
    [Fact]
    public async Task Translates_cosine_distance_when_queried_through_ef_core_10()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var vector = UnitVector(seed: 1);
        await PutAsync(container, WithClip(vector));
        await ImportAsync(db, container);
        var same = new Vector(vector);
        var opposite = new Vector(vector.Select(v => -v).ToArray());

        var distances = await db.AssetEmbeddings
            .Select(e => new { Same = e.Embedding.CosineDistance(same), Opposite = e.Embedding.CosineDistance(opposite) })
            .SingleAsync();

        Assert.Equal(0, distances.Same, 4);
        Assert.Equal(2, distances.Opposite, 4);
    }

    private static Manifest WithClip(float[] vector) => Photo(Source("HD da Ana", "2019/praia.jpg")) with
    {
        Embeddings = new Dictionary<string, JsonNode> { [ClipImageEmbedder.ModelKey] = EmbeddingCodec.Encode(vector) },
    };

    private static float[] UnitVector(int seed)
    {
        var random = new Random(seed);
        var vector = new float[ClipImageEmbedder.Dimensions];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(random.NextDouble() - 0.5);
        }

        EmbeddingCodec.Normalize(vector);
        return vector;
    }

    private static Task<ImportResult> ImportAsync(CatalogDbContext db, BlobContainerClient container) =>
        new ManifestImporter(db, new ManifestValidator(), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(container, CancellationToken.None);

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
