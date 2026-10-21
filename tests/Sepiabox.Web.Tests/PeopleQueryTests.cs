using Pgvector;
using Sepiabox.Catalog;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The people page's query on a real catalog (S-019), with synthetic embeddings only.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class PeopleQueryTests(CatalogFixture catalog)
{
    [Fact]
    public async Task Lists_people_by_face_count_with_their_best_face_cropped_from_the_thumbnail()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(CatalogData.Photo(Ids.Of(1), null), CatalogData.Photo(Ids.Of(2), null, withDerivatives: false));
        var few = new Person { Name = "Ana", FaceCount = 1 };
        var many = new Person { FaceCount = 3 };
        var none = new Person { Name = "Bruno", FaceCount = 0 };
        db.People.AddRange(few, many, none);
        db.Faces.AddRange(
            Face(Ids.Of(1), few, 0.95, [0.1, 0.1, 0.2, 0.2]),
            Face(Ids.Of(1), many, 0.80, [0.5, 0.5, 0.2, 0.2]),
            Face(Ids.Of(1), many, 0.90, [0.3, 0.3, 0.2, 0.2]),
            // The best score, but excluded by the family: never the cover, even if it still names the person.
            Face(Ids.Of(1), many, 0.99, [0.7, 0.1, 0.2, 0.2], FaceAssignments.Excluded),
            Face(Ids.Of(2), many, 0.85, [0.1, 0.1, 0.2, 0.2]));
        await db.SaveChangesAsync();

        var people = await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db))).GetPeopleAsync();

        Assert.Equal([(many.Id, (string?)null, 3), (few.Id, "Ana", 1)], people.Select(p => (p.Id, p.Name, p.FaceCount)));
        var cover = people[0].Cover!;
        Assert.Equal((Ids.Of(1), 256, 192), (cover.AssetId, cover.ThumbnailWidth, cover.ThumbnailHeight));
        Assert.Equal([0.3, 0.3, 0.2, 0.2], cover.Box);
    }

    [Fact]
    public async Task Shows_no_crop_when_the_best_face_s_asset_has_no_thumbnail()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(2), null, withDerivatives: false));
        var person = new Person { FaceCount = 1 };
        db.People.Add(person);
        db.Faces.Add(Face(Ids.Of(2), person, 0.9, [0.1, 0.1, 0.2, 0.2]));
        await db.SaveChangesAsync();

        var people = await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db))).GetPeopleAsync();

        Assert.Null(Assert.Single(people).Cover);
    }

    [Fact]
    public async Task Shows_a_person_s_faces_best_first_without_the_excluded_ones_and_who_they_can_be_merged_with()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(1), null));
        var person = new Person { Name = "Ana", UserEmail = "ana@example.com", FaceCount = 2 };
        var named = new Person { Name = "Bruno", FaceCount = 0 };
        var unnamedEmpty = new Person { FaceCount = 0 };
        var other = new Person { FaceCount = 1 };
        db.People.AddRange(person, named, unnamedEmpty, other);
        db.Faces.AddRange(
            Face(Ids.Of(1), person, 0.80, [0.5, 0.5, 0.2, 0.2]),
            Face(Ids.Of(1), person, 0.90, [0.1, 0.1, 0.2, 0.2]),
            Face(Ids.Of(1), person, 0.99, [0.3, 0.3, 0.2, 0.2], FaceAssignments.Excluded),
            Face(Ids.Of(1), other, 0.70, [0.7, 0.7, 0.2, 0.2]));
        await db.SaveChangesAsync();

        var detail = (await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db))).GetPersonAsync(person.Id))!;

        Assert.Equal(("Ana", "ana@example.com", 2), (detail.Name, detail.UserEmail, detail.FaceCount));
        Assert.Equal([0.1, 0.5], detail.Faces.Select(f => f.Crop!.Box[0]));
        Assert.Equal([other.Id, named.Id], detail.Others.Select(o => o.Id));
        Assert.Null(await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db))).GetPersonAsync(12345));
    }

    private static Face Face(string assetId, Person person, double score, double[] box, string assignment = FaceAssignments.Auto)
    {
        var embedding = new float[128];
        embedding[0] = 1;
        return new Face { AssetId = assetId, Person = person, Score = score, Box = box, Embedding = new Vector(embedding), Assignment = assignment };
    }
}
