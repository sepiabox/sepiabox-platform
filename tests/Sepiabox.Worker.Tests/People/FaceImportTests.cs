using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Importing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests.People;

/// <summary>
/// Faces grouped into people as manifests are imported (S-019), on a real PostgreSQL with pgvector. Every embedding
/// is a synthetic vector: no real face is used.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class FaceImportTests(CatalogFixture fixture)
{
    private static readonly double[] s_box = [0.10, 0.20, 0.30, 0.40];

    [Fact]
    public async Task Puts_two_faces_with_nearly_identical_embeddings_in_the_same_person()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        await PutAsync(container, WithFaces(1, Face(s_box, face)));
        await PutAsync(container, WithFaces(2, Face(s_box, SyntheticFaces.Near(face, seed: 2))));

        var result = await ImportAsync(db, container);

        Assert.Equal(2, result.Imported);
        Assert.Empty(result.Warnings);
        var person = await db.People.SingleAsync();
        Assert.Equal(2, person.FaceCount);
        Assert.All(await db.Faces.ToListAsync(), f => Assert.Equal((person.Id, FaceAssignments.Auto), (f.PersonId!.Value, f.Assignment)));
        Assert.True(FaceMath.Cosine(person.Centroid!.ToArray(), face) > 0.99);
    }

    [Fact]
    public async Task Puts_two_distant_faces_in_different_people()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1))));
        await PutAsync(container, WithFaces(2, Face(s_box, SyntheticFaces.Unit(seed: 3))));

        await ImportAsync(db, container);

        var people = await db.People.ToListAsync();
        Assert.Equal(2, people.Count);
        Assert.All(people, p => Assert.Equal(1, p.FaceCount));
    }

    [Fact]
    public async Task Puts_two_alike_faces_of_one_photo_in_two_people()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        await PutAsync(container, WithFaces(1, Face(s_box, face), Face([0.6, 0.2, 0.3, 0.4], SyntheticFaces.Near(face, seed: 2))));

        await ImportAsync(db, container);

        Assert.Equal([1, 1], await db.People.Select(p => p.FaceCount).ToListAsync());
    }

    [Fact]
    public async Task Gives_a_photo_s_faces_each_their_own_existing_person_most_similar_first()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        await PutAsync(container, WithFaces(1, Face(s_box, face)));
        await ImportAsync(db, container);
        var first = await db.People.AsNoTracking().SingleAsync();

        // Both are like the person; only the closer one joins it.
        var closer = SyntheticFaces.Near(face, seed: 2, noise: 0.01);
        await PutAsync(container, WithFaces(2, Face(s_box, SyntheticFaces.Near(face, seed: 3, noise: 0.2)), Face([0.6, 0.2, 0.3, 0.4], closer)));
        await ImportAsync(db, container);

        Assert.Equal(2, await db.People.CountAsync());
        var joined = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == WithFaces(2).Id && f.PersonId == first.Id);
        Assert.Equal([0.6, 0.2, 0.3, 0.4], joined.Box);
    }

    [Theory]
    [InlineData(0.45, true)]
    [InlineData(0.35, false)]
    public async Task Joins_a_person_at_the_default_threshold_of_0_40(double cosine, bool joins)
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        // cos θ·e1 + sin θ·e2 has exactly this cosine with e1.
        var e1 = new float[128];
        e1[0] = 1;
        var tilted = new float[128];
        tilted[0] = (float)cosine;
        tilted[1] = (float)Math.Sqrt(1 - (cosine * cosine));
        await PutAsync(container, WithFaces(1, Face(s_box, e1)));
        await PutAsync(container, WithFaces(2, Face(s_box, tilted)));

        await ImportAsync(db, container);

        Assert.Equal(joins ? 1 : 2, await db.People.CountAsync());
    }

    [Fact]
    public async Task Computes_the_centroid_as_the_normalized_mean_of_the_person_s_faces()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        float[][] faces = [face, SyntheticFaces.Near(face, seed: 2, noise: 0.1), SyntheticFaces.Near(face, seed: 3, noise: 0.1)];
        for (var n = 0; n < faces.Length; n++)
        {
            await PutAsync(container, WithFaces(n + 1, Face(s_box, faces[n])));
            await ImportAsync(db, container);
        }

        var person = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(3, person.FaceCount);
        var expected = FaceMath.Centroid(faces)!;
        var centroid = person.Centroid!.ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], centroid[i], 5);
        }
    }

    [Fact]
    public async Task Assigns_one_after_the_other_when_two_imports_run_at_once()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        await PutAsync(container, WithFaces(1, Face(s_box, face)));
        await ImportAsync(db, container);
        var connection = fixture.ConnectionStringFor(db);
        var manifests = Enumerable.Range(2, 8).Select(n => WithFaces(n, Face(s_box, SyntheticFaces.Near(face, seed: n)))).ToList();
        foreach (var manifest in manifests)
        {
            await PutAsync(container, manifest);
        }

        // Two importers on their own connections, each taking every other manifest at the same time.
        await Task.WhenAll(new[] { 0, 1 }.Select(half => Task.Run(async () =>
        {
            await using var other = Context(connection);
            foreach (var manifest in manifests.Where((_, i) => i % 2 == half))
            {
                var result = await Importer(other).ImportOneAsync(container, BlobLayout.ManifestBlobName(manifest.Id), CancellationToken.None);
                Assert.Empty(result.Problems);
            }
        })));

        var person = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(9, person.FaceCount);
        var expected = FaceMath.Centroid([face, .. manifests.Select((_, i) => SyntheticFaces.Near(face, seed: i + 2))])!;
        Assert.True(FaceMath.Cosine(expected, person.Centroid!.ToArray()) > 0.99999);
    }

    [Fact]
    public async Task Keeps_an_unnamed_person_whose_faces_were_all_excluded()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1)));
        await PutAsync(container, manifest);
        await ImportAsync(db, container);
        // The family said "not this person" (S-020): the face still names them, excluded.
        await db.Faces.ExecuteUpdateAsync(set => set.SetProperty(f => f.Assignment, FaceAssignments.Excluded));

        await ImportOneAsync(db, container, manifest);

        var person = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(0, person.FaceCount);
        Assert.Null(person.Centroid);
        var face = await db.Faces.AsNoTracking().SingleAsync();
        Assert.Equal((person.Id, FaceAssignments.Excluded), (face.PersonId!.Value, face.Assignment));
    }

    [Fact]
    public async Task Does_not_duplicate_faces_or_people_when_the_same_manifest_is_imported_twice()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1)), Face([0.6, 0.2, 0.3, 0.4], SyntheticFaces.Unit(seed: 3)));
        await PutAsync(container, manifest);
        await ImportOneAsync(db, container, manifest);
        var faces = await db.Faces.AsNoTracking().OrderBy(f => f.Id).Select(f => new { f.Id, f.PersonId }).ToListAsync();

        var again = await ImportOneAsync(db, container, manifest);

        Assert.Equal(1, again.Imported);
        Assert.Equal(faces, await db.Faces.AsNoTracking().OrderBy(f => f.Id).Select(f => new { f.Id, f.PersonId }).ToListAsync());
        Assert.Equal(2, await db.People.CountAsync());
        Assert.All(await db.People.ToListAsync(), p => Assert.Equal(1, p.FaceCount));
    }

    [Fact]
    public async Task Keeps_the_stored_faces_when_a_manifest_did_not_look_for_faces()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1))));
        await ImportAsync(db, container);

        var notLooked = WithFaces(1) with { FacesProcessed = false, FacesVersion = null };
        await PutAsync(container, notLooked);
        await ImportOneAsync(db, container, notLooked);

        Assert.Equal(1, await db.Faces.CountAsync());
        Assert.Equal(1, (await db.People.SingleAsync()).FaceCount);
        Assert.Equal(1, (await db.Assets.SingleAsync()).FacesVersion);
    }

    [Fact]
    public async Task Removes_the_faces_and_the_unnamed_person_when_a_new_look_finds_none()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1))));
        await ImportAsync(db, container);

        var none = WithFaces(1);
        await PutAsync(container, none);
        await ImportOneAsync(db, container, none);

        Assert.Equal(0, await db.Faces.CountAsync());
        Assert.Equal(0, await db.People.CountAsync());
    }

    [Fact]
    public async Task Keeps_a_named_person_with_no_faces_left()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1))));
        await ImportAsync(db, container);
        await db.People.ExecuteUpdateAsync(set => set.SetProperty(p => p.Name, "Ana"));

        var none = WithFaces(1);
        await PutAsync(container, none);
        await ImportOneAsync(db, container, none);

        var person = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(("Ana", 0), (person.Name, person.FaceCount));
        Assert.Null(person.Centroid);
    }

    [Fact]
    public async Task Keeps_the_family_s_choice_when_the_same_face_is_found_again_in_a_slightly_moved_box()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1))));
        await ImportAsync(db, container);
        // The family moved the face to someone else by hand (S-020).
        var chosen = new Person { Name = "Bruno", FaceCount = 0 };
        db.People.Add(chosen);
        await db.SaveChangesAsync();
        await db.Faces.ExecuteUpdateAsync(set => set
            .SetProperty(f => f.PersonId, chosen.Id)
            .SetProperty(f => f.Assignment, FaceAssignments.Manual));
        // As the family's correction (S-020) leaves it: the person the importer made had only that face.
        await db.People.Where(p => p.Id != chosen.Id).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();

        // Another computer's run: the box moved a little and the embedding changed.
        var again = WithFaces(1, Face([0.11, 0.21, 0.30, 0.40], SyntheticFaces.Unit(seed: 5)));
        await PutAsync(container, again);
        await ImportOneAsync(db, container, again);

        var face = await db.Faces.AsNoTracking().SingleAsync();
        Assert.Equal((chosen.Id, FaceAssignments.Manual), (face.PersonId!.Value, face.Assignment));
        Assert.Equal([0.11, 0.21, 0.30, 0.40], face.Box);
        var bruno = await db.People.AsNoTracking().SingleAsync(p => p.Id == chosen.Id);
        Assert.Equal(1, bruno.FaceCount);
        Assert.Equal(1, await db.People.CountAsync());
    }

    [Fact]
    public async Task Keeps_an_excluded_face_excluded_and_out_of_every_centroid()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1)));
        await PutAsync(container, manifest);
        await ImportAsync(db, container);
        await db.Faces.ExecuteUpdateAsync(set => set
            .SetProperty(f => f.PersonId, (long?)null)
            .SetProperty(f => f.Assignment, FaceAssignments.Excluded));
        // As the family's correction (S-020) leaves it: the person had only that face.
        await db.People.ExecuteDeleteAsync();

        await ImportOneAsync(db, container, manifest);

        var face = await db.Faces.AsNoTracking().SingleAsync();
        Assert.Equal(((long?)null, FaceAssignments.Excluded), (face.PersonId, face.Assignment));
        Assert.Equal(0, await db.People.CountAsync());
    }

    [Fact]
    public async Task Never_groups_faces_of_two_face_versions_together()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        await PutAsync(container, WithFaces(1, Face(s_box, face)));
        await PutAsync(container, WithFaces(2, Face(s_box, face)) with { FacesVersion = 2 });

        await ImportAsync(db, container);

        var people = await db.People.OrderBy(p => p.FacesVersion).ToListAsync();
        Assert.Equal([1, 2], people.Select(p => p.FacesVersion));
        Assert.All(people, p => Assert.Equal(1, p.FaceCount));
    }

    [Fact]
    public async Task Replaces_older_faces_with_a_newer_version_and_never_the_other_way()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var old = WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1)));
        await PutAsync(container, old);
        await ImportAsync(db, container);
        var newer = WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 7))) with { FacesVersion = 2 };
        await PutAsync(container, newer);
        await ImportOneAsync(db, container, newer);

        // The same face (same box) keeps its person, whose centroid now comes from version 2 alone.
        var person = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal((2, 1), (person.FacesVersion, person.FaceCount));
        Assert.True(FaceMath.Cosine(person.Centroid!.ToArray(), SyntheticFaces.Unit(seed: 7)) > 0.999);
        Assert.Equal(2, (await db.Assets.AsNoTracking().SingleAsync()).FacesVersion);

        await PutAsync(container, old);
        await ImportOneAsync(db, container, old);

        Assert.Equal(2, (await db.Assets.AsNoTracking().SingleAsync()).FacesVersion);
        var stored = await db.Faces.AsNoTracking().SingleAsync();
        Assert.True(FaceMath.Cosine(stored.Embedding.ToArray(), SyntheticFaces.Unit(seed: 7)) > 0.999);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    public async Task Leaves_out_a_face_whose_embedding_is_unusable_with_one_warning(float value)
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var broken = value == 0f ? new float[128] : SyntheticFaces.Unit(seed: 1);
        broken[3] = value;
        await PutAsync(container, WithFaces(1, Face(s_box, broken), Face([0.6, 0.2, 0.3, 0.4], SyntheticFaces.Unit(seed: 3))));

        var result = await ImportAsync(db, container);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Problems);
        Assert.Contains("1 face whose embedding", Assert.Single(result.Warnings).Reason, StringComparison.Ordinal);
        Assert.Equal(1, await db.Faces.CountAsync());
    }

    [Fact]
    public async Task Uses_the_configured_threshold()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        await PutAsync(container, WithFaces(1, Face(s_box, face)));
        await PutAsync(container, WithFaces(2, Face(s_box, SyntheticFaces.Near(face, seed: 2))));

        await ImportAsync(db, container, FaceAssignerOptions.For(1.0));

        Assert.Equal(2, await db.People.CountAsync());
    }

    [Fact]
    public async Task Imports_faces_of_manifests_imported_before_the_people_migration_on_the_next_import()
    {
        await using var db = await fixture.CreateEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261020233214_QuestionUsage");
        var container = await fixture.CreateContainerAsync();
        var manifest = WithFaces(1, Face(s_box, SyntheticFaces.Unit(seed: 1)));
        await PutAsync(container, manifest);
        // As the import before S-019 left it: the blob's version recorded, its faces never read.
        var etag = (await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id)).GetPropertiesAsync()).Value.ETag.ToString().Trim('"');
        await db.Database.ExecuteSqlAsync($"INSERT INTO manifest_imports (blob_name, etag, imported_at) VALUES ({BlobLayout.ManifestBlobName(manifest.Id)}, {etag}, now())");

        await migrator.MigrateAsync();
        var result = await ImportAsync(db, container);

        Assert.Equal((1, 0), (result.Imported, result.Unchanged));
        Assert.Equal(1, await db.Faces.CountAsync());
    }

    [Fact]
    public async Task The_centroid_index_is_hnsw_with_cosine_distance()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var indexDefinition = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_people_centroid'")
            .SingleAsync();

        Assert.Contains("USING hnsw", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("vector_cosine_ops", indexDefinition, StringComparison.Ordinal);
    }

    /// <summary>A photo whose id is made from <paramref name="n"/>, looked for faces by version 1 and holding these.</summary>
    internal static Manifest WithFaces(int n, params ManifestFace[] faces)
    {
        var id = n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
        return Photo(Source("HD da Ana", $"2019/{n}.jpg")) with
        {
            Id = id,
            Derivatives = [Derivative(id, "thumb-256", 256, 192), Derivative(id, "preview-1280", 1280, 960)],
            FacesProcessed = true,
            FacesVersion = 1,
            Faces = faces,
        };
    }

    internal static ManifestFace Face(double[] box, float[] embedding) => new(box, 0.9, EmbeddingCodec.Encode(embedding));

    private static ManifestDerivative Derivative(string id, string name, int width, int height) =>
        new(name, $"derivatives/{id}/{name}.webp", width, height, "image/webp");

    private static Task<ImportResult> ImportAsync(CatalogDbContext db, BlobContainerClient container, FaceAssignerOptions? options = null) =>
        Importer(db, options).ImportOnceAsync(container, CancellationToken.None);

    private static Task<ImportResult> ImportOneAsync(CatalogDbContext db, BlobContainerClient container, Manifest manifest) =>
        Importer(db).ImportOneAsync(container, BlobLayout.ManifestBlobName(manifest.Id), CancellationToken.None);

    private static CatalogDbContext Context(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, connectionString);
        return new CatalogDbContext(options.Options);
    }

    private static ManifestImporter Importer(CatalogDbContext db, FaceAssignerOptions? options = null) =>
        new(db, new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance, options);

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
