using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Storage;
using Sepiabox.Worker.Assets;
using Sepiabox.Worker.Importing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests.Assets;

/// <summary>
/// <c>assets delete --source</c> (S-057, D-108) on a real PostgreSQL and Azurite: only what came from one source goes,
/// never an asset another source still needs, and an interrupted or raced deletion is safe to run again.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class AssetDeletionTests(CatalogFixture fixture)
{
    private const string Wrong = "wrong-drive";
    private const string Other = "HD da Ana";

    [Fact]
    public async Task A_dry_run_lists_exactly_the_assets_of_the_source_with_their_paths_and_blobs_and_changes_nothing()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "2019/a.jpg"));
        await world.SeedAsync(2, (Wrong, "2019/b.jpg"));
        await world.SeedAsync(3, (Other, "2019/c.jpg"));
        await world.ImportAsync();
        var before = await world.SnapshotAsync();
        var output = new StringWriter();

        var exit = await AssetsDeleteCommand.RunAsync(world.Deletion(), Wrong, yes: false, expect: null, output, TextWriter.Null, default);

        Assert.Equal(0, exit);
        var plan = await world.Deletion().PlanAsync(Wrong, default);
        Assert.Equal([World.Id(1), World.Id(2)], plan.Matched.Select(a => a.Id));
        Assert.Equal(["2019/a.jpg"], plan.Matched[0].SourcePaths);
        Assert.Equal(
            [$"manifests/{World.Id(1)}.json", $"derivatives/{World.Id(1)}/preview-1280.webp", $"derivatives/{World.Id(1)}/thumb-256.webp", $"originals/{World.Id(1)}"],
            plan.Matched[0].Blobs.Order(StringComparer.Ordinal));
        Assert.Equal(8, plan.BlobCount);
        Assert.Equal(2 * 2_345_678, plan.OriginalBytes);
        var text = output.ToString();
        Assert.Contains("Dry run: nothing is deleted. 2 assets", text, StringComparison.Ordinal);
        Assert.Contains("from 2019/a.jpg", text, StringComparison.Ordinal);
        Assert.Contains($"blob originals/{World.Id(1)}", text, StringComparison.Ordinal);
        Assert.Contains("--yes --expect 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain(World.Id(3), text, StringComparison.Ordinal);
        Assert.Equal(before, await world.SnapshotAsync());
    }

    [Fact]
    public async Task Refuses_to_start_when_the_count_is_not_the_one_expected_and_deletes_nothing()
    {
        await using var world = await World.CreateAsync(fixture);
        for (var n = 1; n <= 4; n++)
        {
            await world.SeedAsync(n, (Wrong, $"2019/{n}.jpg"));
        }

        await world.ImportAsync();
        var before = await world.SnapshotAsync();
        var error = new StringWriter();

        var exit = await AssetsDeleteCommand.RunAsync(world.Deletion(), Wrong, yes: true, expect: 3, TextWriter.Null, error, default);

        Assert.Equal(2, exit);
        Assert.Contains("4 assets match, not 3", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, await world.SnapshotAsync());
    }

    [Fact]
    public async Task Deletes_the_matched_assets_rows_manifests_derivatives_and_originals_and_nothing_else()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "2019/a.jpg"));
        await world.SeedAsync(2, (Wrong, "2019/b.jpg"));
        await world.SeedAsync(3, (Wrong, "2019/c.jpg"));
        await world.SeedAsync(4, (Other, "2019/d.jpg"));
        await world.SeedAsync(5, (Other, "2019/e.jpg"), (Wrong, "x/e.jpg"));
        await world.ImportAsync();
        await world.AddRelatedRowsAsync(1);
        var assets = await world.Db.Assets.CountAsync();
        var sources = await world.Db.AssetSources.CountAsync();
        var derivatives = await world.Db.Derivatives.CountAsync();
        var imports = await world.Db.ManifestImports.CountAsync();
        var untouched = await world.BlobsOfAsync(4, 5);
        var output = new StringWriter();

        var exit = await AssetsDeleteCommand.RunAsync(world.Deletion(), Wrong, yes: true, expect: 3, output, TextWriter.Null, default);

        Assert.Equal(0, exit);
        Assert.Contains("Deleted 3 of 3 assets.", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(assets - 3, await world.Db.Assets.CountAsync());
        Assert.Equal(sources - 3, await world.Db.AssetSources.CountAsync());
        Assert.Equal(derivatives - 6, await world.Db.Derivatives.CountAsync());
        Assert.Equal(imports - 3, await world.Db.ManifestImports.CountAsync());
        Assert.Equal([World.Id(4), World.Id(5)], await world.Db.Assets.OrderBy(a => a.Id).Select(a => a.Id).ToListAsync());
        Assert.Equal(0, await world.Db.AssetEmbeddings.CountAsync(e => e.AssetId == World.Id(1)));
        Assert.Equal(0, await world.Db.Faces.CountAsync(f => f.AssetId == World.Id(1)));
        Assert.Equal(0, await world.Db.OccasionAssets.CountAsync(l => l.AssetId == World.Id(1)));
        Assert.Empty(await world.BlobsOfAsync(1, 2, 3));
        Assert.Equal(untouched, await world.BlobsOfAsync(4, 5));
    }

    [Fact]
    public async Task Keeps_an_asset_whose_stored_manifest_also_lists_another_source_though_the_catalog_does_not()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "2019/a.jpg"), (Other, "2019/a.jpg"));
        await world.ImportAsync();
        // The catalog and the blob disagree: the catalog lost its other source.
        await world.Db.AssetSources.Where(s => s.Name == Other).ExecuteDeleteAsync();
        var before = await world.SnapshotAsync();
        var output = new StringWriter();

        var plan = await world.Deletion().PlanAsync(Wrong, default);
        var exit = await AssetsDeleteCommand.RunAsync(world.Deletion(), Wrong, yes: true, expect: 0, output, TextWriter.Null, default);

        Assert.Empty(plan.Matched);
        var kept = Assert.Single(plan.Kept);
        Assert.Equal(World.Id(1), kept.Id);
        Assert.Contains("'HD da Ana'", kept.Reason, StringComparison.Ordinal);
        Assert.Equal(1, exit);
        Assert.Contains(World.Id(1), output.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, await world.SnapshotAsync());
    }

    [Fact]
    public async Task Keeps_an_asset_whose_manifest_cannot_be_trusted()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "2019/a.jpg"));
        await world.ImportAsync();
        await world.Store.WriteAsync(BlobLayout.ManifestsContainer, $"{World.Id(1)}.json", BinaryData.FromString("{ not json"), "application/json", BlobWriteCondition.Always);

        var plan = await world.Deletion().PlanAsync(Wrong, default);

        Assert.Empty(plan.Matched);
        Assert.Contains("cannot be read", Assert.Single(plan.Kept).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deletes_an_asset_with_two_sources_of_the_same_name()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "2019/a.jpg"), (Wrong, "copy/a.jpg"));
        await world.ImportAsync();

        var plan = await world.Deletion().PlanAsync(Wrong, default);
        var result = await world.Deletion().DeleteAsync(plan, default);

        Assert.Equal(["2019/a.jpg", "copy/a.jpg"], plan.Matched.Single().SourcePaths);
        Assert.Equal(1, result.Deleted);
        Assert.True(result.Clean);
        Assert.Equal(0, await world.Db.Assets.CountAsync());
    }

    [Fact]
    public async Task A_manifest_changed_between_the_read_and_the_delete_keeps_the_asset_whole_and_a_rerun_handles_it()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "2019/a.jpg"));
        await world.ImportAsync();
        var before = await world.SnapshotAsync();
        var racing = new Racing(world.Store, beforeManifestDelete: async inner =>
        {
            // Someone uploads the same manifest again with a caption (a new version of the blob).
            var current = (await inner.ReadAsync(BlobLayout.ManifestsContainer, $"{World.Id(1)}.json"))!;
            var changed = ManifestJson.Deserialize(current.Content.ToString()) with { Caption = "added meanwhile" };
            await inner.WriteAsync(BlobLayout.ManifestsContainer, $"{World.Id(1)}.json", BinaryData.FromString(ManifestJson.Serialize(changed)), "application/json", BlobWriteCondition.Always);
        });
        var plan = await world.Deletion(racing).PlanAsync(Wrong, default);

        var first = await world.Deletion(racing).DeleteAsync(plan, default);

        Assert.Equal(0, first.Deleted);
        Assert.Contains("changed while", Assert.Single(first.Kept).Reason, StringComparison.Ordinal);
        Assert.Equal(before.Rows, (await world.SnapshotAsync()).Rows);
        Assert.Equal(before.Blobs, (await world.SnapshotAsync()).Blobs.Where(b => !b.StartsWith("manifests/", StringComparison.Ordinal)).ToList().Concat(before.Blobs.Where(b => b.StartsWith("manifests/", StringComparison.Ordinal))).Order(StringComparer.Ordinal).ToList());

        var rerun = await world.Deletion().DeleteAsync(await world.Deletion().PlanAsync(Wrong, default), default);

        Assert.Equal(1, rerun.Deleted);
        Assert.True(rerun.Clean);
        Assert.Equal(0, await world.Db.Assets.CountAsync());
        Assert.Empty(await world.BlobsOfAsync(1));
    }

    [Fact]
    public async Task A_second_source_merged_in_between_means_the_rerun_keeps_the_asset()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "2019/a.jpg"));
        await world.ImportAsync();
        var racing = new Racing(world.Store, beforeManifestDelete: async inner =>
        {
            var current = (await inner.ReadAsync(BlobLayout.ManifestsContainer, $"{World.Id(1)}.json"))!;
            var stored = ManifestJson.Deserialize(current.Content.ToString());
            var merged = stored with { Sources = [.. stored.Sources, new ManifestSource("local", Other, "2019/a.jpg", DateTimeOffset.UnixEpoch)] };
            await inner.WriteAsync(BlobLayout.ManifestsContainer, $"{World.Id(1)}.json", BinaryData.FromString(ManifestJson.Serialize(merged)), "application/json", BlobWriteCondition.Always);
        });

        var first = await world.Deletion(racing).DeleteAsync(await world.Deletion(racing).PlanAsync(Wrong, default), default);
        var plan = await world.Deletion().PlanAsync(Wrong, default);

        Assert.Equal(0, first.Deleted);
        Assert.Empty(plan.Matched);
        Assert.Contains("'HD da Ana'", Assert.Single(plan.Kept).Reason, StringComparison.Ordinal);
        Assert.Equal(1, await world.Db.Assets.CountAsync());
        Assert.NotEmpty(await world.BlobsOfAsync(1));
    }

    [Fact]
    public async Task Lists_an_asset_whose_manifest_was_written_again_after_its_blobs_went_as_needing_an_upload_and_exits_1()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "2019/a.jpg"));
        await world.ImportAsync();
        var manifest = (await world.Store.ReadAsync(BlobLayout.ManifestsContainer, $"{World.Id(1)}.json"))!.Content;
        var racing = new Racing(world.Store, afterOriginalDelete: inner =>
            inner.WriteAsync(BlobLayout.ManifestsContainer, $"{World.Id(1)}.json", manifest, "application/json", BlobWriteCondition.Always));
        var output = new StringWriter();

        var exit = await AssetsDeleteCommand.RunAsync(world.Deletion(racing), Wrong, yes: true, expect: 1, output, TextWriter.Null, default);

        Assert.Equal(1, exit);
        Assert.Contains("Re-upload needed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("from 2019/a.jpg", output.ToString(), StringComparison.Ordinal);
        // Its rows stay, with its manifest, so the next upload and import make it whole again.
        Assert.Equal(1, await world.Db.Assets.CountAsync());
        Assert.Empty(await world.Store.ListAsync(BlobLayout.OriginalsContainer).ToListAsync());
    }

    [Theory]
    [InlineData("manifest only")]
    [InlineData("manifest and derivatives")]
    [InlineData("every blob, no rows deleted")]
    public async Task A_rerun_finishes_a_deletion_that_was_interrupted(string stoppedAfter)
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "2019/a.jpg"));
        await world.ImportAsync();
        var id = World.Id(1);
        await world.Store.DeleteAsync(BlobLayout.ManifestsContainer, $"{id}.json");
        if (stoppedAfter != "manifest only")
        {
            await world.Store.DeleteAsync(BlobLayout.DerivativesContainer, $"{id}/thumb-256.webp");
            if (stoppedAfter.StartsWith("every", StringComparison.Ordinal))
            {
                await world.Store.DeleteAsync(BlobLayout.DerivativesContainer, $"{id}/preview-1280.webp");
                await world.Store.DeleteAsync(BlobLayout.OriginalsContainer, id);
            }
        }

        var plan = await world.Deletion().PlanAsync(Wrong, default);
        var result = await world.Deletion().DeleteAsync(plan, default);

        Assert.True(Assert.Single(plan.Matched).ManifestMissing);
        Assert.Equal(1, result.Deleted);
        Assert.True(result.Clean);
        Assert.Equal(0, await world.Db.Assets.CountAsync());
        Assert.Empty(await world.BlobsOfAsync(1));
        // And a second rerun finds nothing to do.
        Assert.Empty((await world.Deletion().PlanAsync(Wrong, default)).Matched);
    }

    [Fact]
    public async Task Does_not_match_a_source_that_only_looks_like_the_name_or_that_differs_in_case()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, (Wrong, "a.jpg"));
        await world.SeedAsync(2, ("wrong-drive-2", "b.jpg"));
        await world.SeedAsync(3, ("Wrong-Drive", "c.jpg"));
        await world.ImportAsync();

        var plan = await world.Deletion().PlanAsync(Wrong, default);

        Assert.Equal([World.Id(1)], plan.Matched.Select(a => a.Id));
    }

    [Fact]
    public async Task An_index_only_asset_has_no_original_to_delete_and_goes_too()
    {
        await using var world = await World.CreateAsync(fixture);
        await world.SeedAsync(1, withOriginal: false, sources: (Wrong, "a.jpg"));
        await world.ImportAsync();

        var plan = await world.Deletion().PlanAsync(Wrong, default);
        var result = await world.Deletion().DeleteAsync(plan, default);

        Assert.DoesNotContain(plan.Matched.Single().Blobs, b => b.StartsWith("originals/", StringComparison.Ordinal));
        Assert.Equal(0, plan.OriginalBytes);
        Assert.True(result.Clean);
        Assert.Equal(0, await world.Db.Assets.CountAsync());
    }

    /// <summary>A store that does something at a chosen moment of a deletion, as a second computer would.</summary>
    private sealed class Racing(
        IBlobStore inner, Func<IBlobStore, Task>? beforeManifestDelete = null, Func<IBlobStore, Task>? afterOriginalDelete = null)
        : DelegatingBlobStore(inner)
    {
        public override async Task<BlobDeleteStatus> DeleteIfVersionAsync(string container, string name, string version, CancellationToken cancellationToken = default)
        {
            if (beforeManifestDelete is not null)
            {
                await beforeManifestDelete(Inner);
            }

            return await base.DeleteIfVersionAsync(container, name, version, cancellationToken);
        }

        public override async Task<bool> DeleteAsync(string container, string name, CancellationToken cancellationToken = default)
        {
            var deleted = await base.DeleteAsync(container, name, cancellationToken);
            if (afterOriginalDelete is not null && container == BlobLayout.OriginalsContainer)
            {
                await afterOriginalDelete(Inner);
            }

            return deleted;
        }
    }

    private sealed record Snapshot(List<string> Rows, List<string> Blobs);

    private sealed class World : IAsyncDisposable
    {
        private readonly CatalogFixture _fixture;
        private readonly List<Manifest> _pending = [];

        private World(CatalogFixture fixture, CatalogDbContext db)
        {
            _fixture = fixture;
            Db = db;
        }

        public CatalogDbContext Db { get; }

        public IBlobStore Store => _fixture.Store;

        /// <summary>A 64-hex asset id, made unique by <paramref name="n"/>.</summary>
        public static string Id(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

        public static async Task<World> CreateAsync(CatalogFixture fixture)
        {
            // The containers are the app's own names, shared by the whole run: start every test with them empty.
            foreach (var name in new[] { BlobLayout.ManifestsContainer, BlobLayout.DerivativesContainer, BlobLayout.OriginalsContainer })
            {
                var container = fixture.Blobs.GetBlobContainerClient(name);
                await container.CreateIfNotExistsAsync();
                await foreach (var blob in fixture.Store.ListAsync(name))
                {
                    await fixture.Store.DeleteAsync(name, blob.Name);
                }
            }

            return new World(fixture, await fixture.CreateMigratedDatabaseAsync());
        }

        public AssetDeletion Deletion(IBlobStore? store = null) =>
            new(Db, store ?? Store, new ManifestValidator(), NullLogger.Instance);

        /// <summary>Writes the asset's manifest, derivatives and original to storage; <see cref="ImportAsync"/> makes its rows.</summary>
        public Task SeedAsync(int n, params (string Name, string Path)[] sources) => SeedAsync(n, true, sources);

        public async Task SeedAsync(int n, bool withOriginal, params (string Name, string Path)[] sources)
        {
            var id = Id(n);
            var manifest = Photo(Source(sources[0].Name, sources[0].Path)) with
            {
                Id = id,
                Sources = [.. sources.Select(s => Source(s.Name, s.Path))],
                Derivatives =
                [
                    new ManifestDerivative("thumb-256", $"derivatives/{id}/thumb-256.webp", 256, 192, "image/webp"),
                    new ManifestDerivative("preview-1280", $"derivatives/{id}/preview-1280.webp", 1280, 960, "image/webp"),
                ],
                OriginalStored = withOriginal ? null : false,
            };
            var written = await new ManifestStore(new ManifestValidator(), Store).WriteMergedAsync(BlobLayout.ManifestsContainer, manifest, default);
            Assert.Equal(ManifestWriteStatus.Written, written.Status);
            foreach (var derivative in manifest.Derivatives)
            {
                await Store.WriteAsync(BlobLayout.DerivativesContainer, BlobLayout.DerivativeBlobName(id, derivative.Path), BinaryData.FromString("webp"), derivative.ContentType, BlobWriteCondition.Always);
            }

            if (withOriginal)
            {
                await Store.WriteAsync(BlobLayout.OriginalsContainer, id, BinaryData.FromString("original bytes"), "image/jpeg", BlobWriteCondition.Always);
            }

            _pending.Add(manifest);
        }

        public async Task ImportAsync()
        {
            var result = await new ManifestImporter(
                    Db, Store, new ManifestValidator(), new PostgisPlaceResolver(Db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
                .ImportOnceAsync(BlobLayout.ManifestsContainer, default);
            Assert.Empty(result.Problems);
            Assert.Equal(_pending.Count, await Db.Assets.CountAsync());
            Db.ChangeTracker.Clear();
        }

        // What the asset has besides its sources and derivatives: an embedding, a face and a place in an occasion.
        public async Task AddRelatedRowsAsync(int n)
        {
            var id = Id(n);
            Db.AssetEmbeddings.Add(new AssetEmbedding { AssetId = id, Model = "clip-vit-b32", Embedding = new Vector(new float[512]) });
            var face = new float[128];
            face[0] = 1;
            Db.Faces.Add(new Face { AssetId = id, Box = [0.1, 0.1, 0.2, 0.2], Score = 0.9, Embedding = new Vector(face), Assignment = FaceAssignments.Auto });
            var occasion = new Occasion { StartsAt = new DateTime(2019, 7, 14), EndsAt = new DateTime(2019, 7, 14), AssetCount = 1 };
            Db.Occasions.Add(occasion);
            await Db.SaveChangesAsync();
            Db.OccasionAssets.Add(new OccasionAsset { EventId = occasion.Id, AssetId = id });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<List<string>> BlobsOfAsync(params int[] ns)
        {
            var found = new List<string>();
            foreach (var n in ns)
            {
                var id = Id(n);
                foreach (var (container, prefix) in new[] { (BlobLayout.ManifestsContainer, id), (BlobLayout.DerivativesContainer, id + "/"), (BlobLayout.OriginalsContainer, id) })
                {
                    await foreach (var blob in Store.ListAsync(container, prefix))
                    {
                        found.Add($"{container}/{blob.Name}@{blob.Version}");
                    }
                }
            }

            return found;
        }

        public async Task<Snapshot> SnapshotAsync()
        {
            var rows = new List<string>();
            rows.AddRange((await Db.Assets.AsNoTracking().OrderBy(a => a.Id).Select(a => "asset " + a.Id).ToListAsync()));
            rows.AddRange((await Db.AssetSources.AsNoTracking().OrderBy(s => s.AssetId).ThenBy(s => s.Path).Select(s => $"source {s.AssetId} {s.Name} {s.Path}").ToListAsync()));
            rows.AddRange((await Db.Derivatives.AsNoTracking().OrderBy(d => d.BlobPath).Select(d => "derivative " + d.BlobPath).ToListAsync()));
            rows.AddRange((await Db.ManifestImports.AsNoTracking().OrderBy(m => m.BlobName).Select(m => $"import {m.BlobName} {m.ETag}").ToListAsync()));
            var blobs = new List<string>();
            foreach (var container in new[] { BlobLayout.ManifestsContainer, BlobLayout.DerivativesContainer, BlobLayout.OriginalsContainer })
            {
                await foreach (var blob in Store.ListAsync(container))
                {
                    blobs.Add($"{container}/{blob.Name}");
                }
            }

            return new Snapshot(rows, [.. blobs.Order(StringComparer.Ordinal)]);
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }
}
