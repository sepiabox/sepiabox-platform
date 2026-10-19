using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task An_embedding_with_values_that_are_not_numbers_is_reported_and_not_imported()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var vector = UnitVector(seed: 1);
        vector[7] = float.NaN;
        await PutAsync(container, WithClip(vector));

        var result = await ImportAsync(db, container);

        Assert.Contains("clip-vit-b32", Assert.Single(result.Problems).Reason, StringComparison.Ordinal);
        Assert.Equal(0, await db.Assets.CountAsync());
    }

    [Fact]
    public async Task The_embedding_index_answers_nearest_neighbour_queries()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var indexDefinition = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_asset_embeddings_embedding'")
            .SingleAsync();

        Assert.Contains("USING hnsw", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("vector_cosine_ops", indexDefinition, StringComparison.Ordinal);
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
