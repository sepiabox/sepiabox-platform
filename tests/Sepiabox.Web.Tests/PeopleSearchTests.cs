using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>
/// Filtering and counting by people (S-021) on a real PostgreSQL catalog. Faces are synthetic vectors, addresses
/// example.com.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class PeopleSearchTests(CatalogFixture catalog)
{
    private const string Carla = "carla@example.com";
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 19, 9, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task Finds_only_the_photos_with_everyone_named_ignoring_case()
    {
        await using var db = await CatalogAsync(
            (1, ["Ana"]),
            (2, ["Pedro"]),
            (3, ["Ana", "Pedro"]),
            (4, ["Ana", "Pedro", "Carla"]),
            (5, []));

        var found = await IdsAsync(db, new PeopleFilter { All = ["ana", "PEDRO"] });

        Assert.Equal([Ids.Of(4), Ids.Of(3)], found);
    }

    [Theory]
    [InlineData(FaceAssignments.Excluded)]
    [InlineData(FaceAssignments.NotFace)]
    public async Task Never_puts_someone_in_a_photo_through_a_face_that_does_not_count(string assignment)
    {
        await using var db = await CatalogAsync((1, ["Ana"]), (2, ["Ana"]));
        await db.Faces.Where(f => f.AssetId == Ids.Of(2)).ExecuteUpdateAsync(set => set.SetProperty(f => f.Assignment, assignment));

        Assert.Equal([Ids.Of(1)], await IdsAsync(db, new PeopleFilter { All = ["Ana"] }));
    }

    [Fact]
    public async Task Applies_this_is_me_for_the_signed_in_user()
    {
        await using var db = await CatalogAsync((1, ["Ana"]), (2, ["Ana", "Carla"]), (3, ["Carla"]));

        Assert.Equal([Ids.Of(2)], await IdsAsync(db, new PeopleFilter { All = ["Ana"], IncludeMe = true }, user: "Carla@Example.com"));
        Assert.Equal([Ids.Of(3), Ids.Of(2)], await IdsAsync(db, new PeopleFilter { IncludeMe = true }));
    }

    [Theory]
    [InlineData("bruno@example.com")]
    [InlineData(null)]
    public async Task Says_me_is_missing_when_the_user_has_not_said_this_is_me(string? user)
    {
        await using var db = await CatalogAsync((1, ["Carla"]));

        var unknown = await Assert.ThrowsAsync<UnknownPeopleException>(() => IdsAsync(db, new PeopleFilter { IncludeMe = true }, user: user));

        Assert.True(unknown.MeMissing);
        Assert.Empty(unknown.Missing);
    }

    [Fact]
    public async Task Names_every_person_the_catalog_does_not_have()
    {
        await using var db = await CatalogAsync((1, ["Ana"]));

        var unknown = await Assert.ThrowsAsync<UnknownPeopleException>(() => IdsAsync(db, new PeopleFilter { All = ["Ana", "Zé", "Bia"] }));
        var count = await Assert.ThrowsAsync<UnknownPeopleException>(() => Search(db).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { People = new PeopleFilter { All = ["Zé"] } },
            Aggregate = new SearchAggregate(),
        }));

        Assert.Equal(["Zé", "Bia"], unknown.Missing);
        Assert.False(unknown.MeMissing);
        Assert.Equal(["Zé"], count.Missing);
    }

    [Fact]
    public async Task Gives_the_three_people_in_the_most_photos_of_2025_in_order_without_me()
    {
        await using var db = await CatalogAsync(
            (1, ["Carla", "Ana", "Pedro"]),
            (2, ["Carla", "Ana"]),
            (3, ["Carla", "Ana", "Bruno"]),
            (4, ["Carla", "Pedro", "Dora"]),
            (5, ["Carla", "Bruno"]),
            (6, ["Carla", "Ana"]),
            // Another year, and someone unnamed: neither counts.
            (7, ["Carla", "Dora", "Dora2"]));
        await db.Assets.Where(a => a.Id == Ids.Of(7)).ExecuteUpdateAsync(set => set.SetProperty(a => a.CapturedAt, new DateTime(2024, 1, 1)));
        await db.People.Where(p => p.Name == "Dora2").ExecuteUpdateAsync(set => set.SetProperty(p => p.Name, (string?)null));

        var count = await Search(db, Carla).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { Years = [2025], People = new PeopleFilter { IncludeMe = true } },
            Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Person, Top = 3 },
        });

        Assert.Equal(6, count!.Total);
        Assert.Equal([new CountGroup("Ana", 4), new CountGroup("Bruno", 2), new CountGroup("Pedro", 2)], count.Groups);
        Assert.Equal(4, count.AllGroups);
    }

    [Fact]
    public async Task Counts_a_photo_once_for_a_person_with_two_counted_faces_in_it()
    {
        await using var db = await CatalogAsync((1, ["Ana", "Ana"]), (2, ["Ana"]), (3, ["Pedro"]));

        var count = await Search(db).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters(),
            Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Person },
        });

        Assert.Equal(2, await db.Faces.CountAsync(f => f.AssetId == Ids.Of(1)));
        Assert.Equal([new CountGroup("Ana", 2), new CountGroup("Pedro", 1)], count!.Groups);
    }

    [Fact]
    public async Task Says_me_is_missing_alongside_a_known_name_for_a_user_who_has_not_said_this_is_me()
    {
        await using var db = await CatalogAsync((1, ["Ana", "Carla"]));

        var unknown = await Assert.ThrowsAsync<UnknownPeopleException>(
            () => IdsAsync(db, new PeopleFilter { All = ["Ana"], IncludeMe = true }, user: "bruno@example.com"));

        Assert.True(unknown.MeMissing);
        Assert.Empty(unknown.Missing);
    }

    [Theory]
    [InlineData("Ana  Maria")]
    [InlineData(" ana maria ")]
    [InlineData("Ana \u200BMaria\t")]
    [InlineData("Jose\u0301")]
    public async Task Finds_a_person_by_a_name_written_with_extra_spaces_invisible_characters_or_decomposed_accents(string name)
    {
        await using var db = await CatalogAsync((1, ["Ana Maria"]), (2, ["Jos\u00E9"]), (3, ["Pedro"]));
        var expected = name.Contains('J', StringComparison.Ordinal) ? Ids.Of(2) : Ids.Of(1);

        Assert.Equal([expected], await IdsAsync(db, new PeopleFilter { All = [name] }));
        var count = await Search(db).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { People = new PeopleFilter { All = [name] } },
            Aggregate = new SearchAggregate(),
        });
        Assert.Equal(1, count!.Total);
    }

    [Fact]
    public async Task Answers_400_with_the_missing_name_when_a_search_names_someone_unknown()
    {
        await using var db = await CatalogAsync((1, ["Ana"]));
        await using var web = new WebFactory(catalog.ConnectionStringFor(db));
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative),
            JsonContent.Create(JsonDocument.Parse("""{"filters":{"people":{"all":["Ana","Zé"]}}}""").RootElement));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["Zé"], problem.GetProperty("missing").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal("filters.people", problem.GetProperty("field").GetString());
        Assert.Contains("Zé", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lists_the_people_of_a_photo_and_the_named_ones_for_the_filter_bar()
    {
        await using var db = await CatalogAsync((1, ["Pedro", "Ana", "Nobody"]), (2, ["Ana"]));
        await db.People.Where(p => p.Name == "Nobody").ExecuteUpdateAsync(set => set.SetProperty(p => p.Name, (string?)null));
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)), s_clock, user: new User(Carla));

        var detail = await queries.GetAssetAsync(Ids.Of(1));
        var options = await queries.GetFilterOptionsAsync();

        Assert.Equal(["Ana", "Pedro", null], detail!.People.Select(p => p.Name));
        Assert.Equal(["Ana", "Pedro"], options.People);
        Assert.False(options.MeLinked);
    }

    private static SearchService Search(CatalogDbContext db, string? user = Carla) => new(db, s_clock, userEmail: user);

    private static async Task<List<string>> IdsAsync(CatalogDbContext db, PeopleFilter people, string? user = Carla) =>
        [.. (await Search(db, user).ExecuteAsync(new SearchQuery { Filters = new SearchFilters { People = people } })).Hits.Select(h => h.Id)];

    // One photo per entry, on its own day of 2025, with a counted face for each person named; Carla is "me".
    private async Task<CatalogDbContext> CatalogAsync(params (int N, string[] People)[] photos)
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        var people = photos.SelectMany(p => p.People).Distinct().ToDictionary(
            name => name, name => new Person { Name = name, UserEmail = name == "Carla" ? Carla : null, FaceCount = 1 });
        db.People.AddRange(people.Values);
        foreach (var (n, names) in photos)
        {
            db.Assets.Add(CatalogData.Photo(Ids.Of(n), new DateTime(2025, 1, n, 12, 0, 0)));
            foreach (var (name, i) in names.Select((name, i) => (name, i)))
            {
                var embedding = new float[128];
                embedding[i] = 1;
                db.Faces.Add(new Face
                {
                    AssetId = Ids.Of(n),
                    Box = [0.1 * i, 0.1, 0.1, 0.1],
                    Score = 0.9,
                    Embedding = new Vector(embedding),
                    Person = people[name],
                    Assignment = FaceAssignments.Auto,
                });
            }
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private sealed class User(string email) : ICurrentUser
    {
        public string? Email => email;
    }
}
