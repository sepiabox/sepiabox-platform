using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The settings page's face recognition switch and a person's "Delete face data" (S-022), in bUnit.</summary>
public sealed class FaceSettingsPageTests : LocalizedContext
{
    private readonly FakeFaceSettings _faces = new();
    private readonly FakeLibrary _library = new();

    public FaceSettingsPageTests()
    {
        Services.AddSingleton<IFaceSettings>(_faces);
        Services.AddSingleton<ILibraryQueries>(_library);
        Services.AddSingleton<IPeopleEditor>(new FakePeopleEditor());
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: null);
    }

    [Fact]
    public void Asks_on_the_page_itself_before_turning_recognition_off_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = Render<Settings>();
        Assert.Equal("Configurações", page.Find("h1").TextContent);
        Assert.StartsWith("O reconhecimento facial está ativado", page.Find(".faces-status").TextContent, StringComparison.Ordinal);
        Assert.Equal(("get", "settings"), (page.Find("form.ask-off").GetAttribute("method"), page.Find("form.ask-off").GetAttribute("action")));
        Assert.Equal("Desativar e apagar os dados faciais", page.Find("form.ask-off button").TextContent);
        Assert.Empty(page.FindAll("form.confirm"));

        Services.GetRequiredService<NavigationManager>().NavigateTo("settings?confirm=faces-off");
        var confirming = Render<Settings>();

        Assert.StartsWith("Desativar o reconhecimento facial? Todos os rostos e todas as pessoas", confirming.Find(".confirm-off .note").TextContent, StringComparison.Ordinal);
        Assert.Equal("post", confirming.Find("form.confirm").GetAttribute("method"));
        Assert.Equal("Sim, desativar e apagar", confirming.Find("form.confirm button").TextContent);
        Assert.Equal("settings", confirming.Find("form.confirm a").GetAttribute("href"));
        Assert.Contains("process --faces", confirming.Find(".work-folder").TextContent, StringComparison.Ordinal);
        Assert.StartsWith("Os backups automáticos", confirming.Find(".backups").TextContent, StringComparison.Ordinal);
        Assert.Contains("das cópias das informações guardadas na nuvem (manifestos)", confirming.Find(".confirm-off .note").TextContent, StringComparison.Ordinal);
        Assert.Empty(_faces.Calls);
    }

    [Fact]
    public void Offers_to_turn_recognition_back_on_only_once_the_manifests_carry_the_erase()
    {
        _faces.State = new FaceRecognitionState(false, DateTimeOffset.UnixEpoch, null);
        var pending = Render<Settings>();

        Assert.Equal("Face recognition is off: no face is imported.", pending.Find(".faces-status").TextContent);
        Assert.StartsWith("The faces are being erased from the copies of the library's information kept in the cloud (manifests) too", pending.Find(".faces-sweep").TextContent, StringComparison.Ordinal);
        Assert.Empty(pending.FindAll("form.turn-on"));

        _faces.State = _faces.State with { SweptThrough = DateTimeOffset.UnixEpoch.AddHours(1) };
        var swept = Render<Settings>();

        Assert.Equal("Turn face recognition on", swept.Find("form.turn-on button").TextContent);
    }

    [Fact]
    public void Lists_who_deleted_face_data_and_how_much_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        _faces.History.Add(Deletion(FaceDataDeletionScopes.Person, 3, 1, new DateTimeOffset(2026, 10, 21, 10, 5, 0, TimeSpan.Zero)));
        _faces.History.Add(Deletion(FaceDataDeletionScopes.Library, 120, 9, new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero)));

        var page = Render<Settings>();

        Assert.Equal(
            [
                "21/10/2026 10:05, por ana@example.com: dados faciais de uma pessoa apagados; rostos: 3.",
                "20/10/2026 08:00, por ana@example.com: reconhecimento desativado; rostos apagados: 120, pessoas apagadas: 9.",
            ],
            page.FindAll(".face-deletions li").Select(li => li.TextContent));
    }

    [Fact]
    public void Words_ignored_faces_apart_from_one_persons_deleted_face_data()
    {
        _faces.History.Add(Deletion(FaceDataDeletionScopes.Ignore, 14, 6, new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero)));
        _faces.History.Add(Deletion(FaceDataDeletionScopes.Person, 3, 1, new DateTimeOffset(2026, 10, 21, 10, 5, 0, TimeSpan.Zero)));

        var page = Render<Settings>();

        var lines = page.FindAll(".face-deletions li").Select(li => li.TextContent).ToList();

        // The time's own format varies with the runtime's culture data; the words are what is asserted.
        Assert.EndsWith(", by ana@example.com: unnamed faces deleted: 14; people deleted: 6.", lines[0], StringComparison.Ordinal);
        Assert.EndsWith(", by ana@example.com: one person's face data deleted; faces: 3.", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Words_ignored_faces_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        _faces.History.Add(Deletion(FaceDataDeletionScopes.Ignore, 14, 6, new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero)));

        Assert.Equal(
            "22/10/2026 09:00, por ana@example.com: rostos sem nome apagados: 14; pessoas apagadas: 6.",
            Assert.Single(Render<Settings>().FindAll(".face-deletions li")).TextContent);
    }

    [Theory]
    [InlineData("faces-off", "Face recognition is off and the face data was deleted.")]
    [InlineData("faces-pending", "Not yet: the faces are still being erased from the copies in the cloud.")]
    public void Shows_what_happened_from_the_note_in_the_address(string note, string expected)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"settings?note={note}");

        Assert.Equal(expected, Render<Settings>().Find(".note").TextContent);
    }

    [Fact]
    public void Offers_delete_face_data_on_a_person_and_asks_first()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var person = Render<Components.Pages.Person>(p => p.Add(x => x.Id, 7L));
        var link = person.Find(".delete-face-data a");
        Assert.Equal(("Apagar dados faciais", "people/7/delete"), (link.TextContent, link.GetAttribute("href")));

        var confirmation = Render<PersonDelete>(p => p.Add(x => x.Id, 7L));

        Assert.Equal("Ana", confirmation.Find(".delete-people .name").TextContent);
        Assert.StartsWith("Esta pessoa, o nome e todos os rostos dela são apagados", confirmation.Find(".note").TextContent, StringComparison.Ordinal);
        Assert.Equal(("post", "Apagar"), (confirmation.Find("form.confirm").GetAttribute("method"), confirmation.Find("form.confirm button").TextContent));
        // The consequence is one bold line, Cancel comes first and has the focus, and the button is the danger one.
        Assert.NotNull(confirmation.Find("p.note.danger strong"));
        var actions = confirmation.Find("form.confirm").Children.Where(c => c.LocalName is "a" or "button").ToList();
        Assert.Equal(["a", "button"], actions.Select(c => c.LocalName));
        Assert.True(actions[0].HasAttribute("autofocus"));
        Assert.Contains("secondary", actions[0].ClassName, StringComparison.Ordinal);
        Assert.Equal("button danger", actions[1].ClassName);
        Assert.NotNull(confirmation.Find(".confirm-panel"));
        Assert.Empty(_faces.Calls);
    }

    private static FaceDataDeletion Deletion(string scope, int faces, int people, DateTimeOffset at) =>
        new() { Scope = scope, Faces = faces, People = people, DeletedAt = at, DeletedBy = "ana@example.com" };
}

/// <summary>The forms over HTTP, signed in, with an in-memory library and a recording switch.</summary>
public sealed partial class FaceSettingsPostTests
{
    private const string Ana = "ana@example.com";

    [Fact]
    public async Task Turns_recognition_off_from_the_confirmation_as_the_signed_in_user()
    {
        var (web, faces) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        Assert.Empty(faces.Calls);
        using var response = await PostAsync(http, "/settings?confirm=faces-off", "off", new() { ["Off"] = "yes" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/settings?note=faces-off", response.Headers.Location?.PathAndQuery);
        Assert.Equal([$"off {Ana}"], faces.Calls);
    }

    [Fact]
    public async Task Says_not_yet_when_turning_on_while_the_manifests_are_pending()
    {
        var (web, faces) = Web(new FaceRecognitionState(false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1)));
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);
        // The form is offered, but the catalog refuses: recognition was turned off again meanwhile.
        faces.RefuseOn = true;

        using var response = await PostAsync(http, "/settings", "on", new() { ["On"] = "yes" });

        Assert.Equal("/settings?note=faces-pending", response.Headers.Location?.PathAndQuery);
        Assert.False(faces.State.On);
    }

    [Fact]
    public async Task Deletes_a_person_s_face_data_only_from_the_confirmation_page()
    {
        var (web, faces) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await PostAsync(http, "/people/7/delete", "delete", new() { ["Confirmed"] = "yes" });

        Assert.Equal("/people?note=deleted", response.Headers.Location?.PathAndQuery);
        Assert.Equal([$"delete 7 {Ana}"], faces.Calls);
    }

    [Fact]
    public async Task Says_there_are_no_people_while_recognition_is_off()
    {
        var (web, _) = Web(new FaceRecognitionState(false, DateTimeOffset.UnixEpoch, null));
        await using var __ = web;
        using var http = web.CreateSignedInClient(Ana);

        var html = WebUtility.HtmlDecode(await http.GetStringAsync(new Uri("/people", UriKind.Relative)));

        Assert.Contains("Face recognition is off, so there are no people.", html, StringComparison.Ordinal);
        Assert.Contains("href=\"settings\" class=\"nav-link\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/settings")]
    [InlineData("/people/7/delete")]
    [InlineData("/places")]
    public async Task Needs_a_sign_in(string address)
    {
        var (web, _) = Web();
        await using var __ = web;
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(new Uri(address, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Answers_404_for_deleting_a_person_who_is_not_there()
    {
        var (web, _) = Web();
        await using var __ = web;
        using var http = web.CreateSignedInClient(Ana);

        using var response = await http.GetAsync(new Uri("/people/12345/delete", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static (WebFactory Web, FakeFaceSettings Faces) Web(FaceRecognitionState? state = null)
    {
        var library = new FakeLibrary();
        library.Persons[7] = PersonPages.Person(7, name: "Ana", user: null);
        var faces = new FakeFaceSettings { State = state ?? FaceRecognitionState.Default };
        return (new WebFactory(library) { AccessEmails = Ana, FaceSettings = faces }, faces);
    }

    // As a browser: the page's antiforgery token, then the form with it.
    internal static async Task<HttpResponseMessage> PostAsync(HttpClient http, string address, string form, Dictionary<string, string> fields)
    {
        var page = await http.GetStringAsync(new Uri(address, UriKind.Relative));
        var match = TokenField().Match(page);
        Assert.True(match.Success, page[..Math.Min(page.Length, 4000)]);
        var token = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        fields["_handler"] = form;
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
        return await http.PostAsync(new Uri(address, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"|type=\"hidden\" name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenField();
}

/// <summary>Turning recognition off from the page on a real PostgreSQL catalog (S-022). Synthetic vectors only.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class FaceSettingsCatalogTests(CatalogFixture catalog)
{
    private const string Ana = "ana@example.com";

    [Fact]
    public async Task Turning_recognition_off_empties_the_people_filter_the_photo_s_people_and_the_translator_s_names()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(1), new DateTime(2019, 7, 14)));
        db.Faces.Add(Face(new Catalog.Person { Name = "Ana", UserEmail = Ana, FaceCount = 1 }));
        await db.SaveChangesAsync();
        var contexts = new TestContexts(catalog.ConnectionStringFor(db));
        var library = new LibraryQueries(contexts, TimeProvider.System, user: new SignedIn(Ana));
        Assert.Equal(["Ana"], (await library.GetFilterOptionsAsync()).People);

        await new FaceSettings(contexts, TimeProvider.System).TurnOffAsync(Ana);

        var options = await library.GetFilterOptionsAsync();
        Assert.Empty(options.People);
        Assert.False(options.MeLinked);
        Assert.Empty((await library.GetAssetAsync(Ids.Of(1)))!.People);
        var translator = new FakeTranslator();
        var questions = new QuestionService(translator, new FakeQuestionQuota(), library, new QuestionLimits(), TimeProvider.System);
        await questions.AskAsync("fotos com a Ana", new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, Ana)], "test")));
        Assert.DoesNotContain(
            Assert.Single(translator.Calls).Context.Lists,
            list => list.DslField == "filters.people.all" && list.Values.Count > 0);
        await using var search = contexts.CreateDbContext();
        var unknown = await Assert.ThrowsAsync<Catalog.Search.UnknownPeopleException>(() => new Catalog.Search.SearchService(search, TimeProvider.System, userEmail: Ana)
            .ExecuteAsync(new Catalog.Search.SearchQuery
            {
                Filters = new Catalog.Search.SearchFilters { People = new Catalog.Search.PeopleFilter { All = ["Ana"], IncludeMe = true } },
            }));
        Assert.Equal(["Ana"], unknown.Missing);
        Assert.True(unknown.MeMissing);
    }

    [Fact]
    public async Task Crops_the_faces_a_photo_would_lose_and_previews_the_people_the_bulk_action_would_delete()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(1), new DateTime(2019, 7, 14)));
        db.Assets.Add(CatalogData.Photo(Ids.Of(2), new DateTime(2019, 7, 15)));
        var named = new Catalog.Person { Name = "Ana", FaceCount = 1 };
        var strangerOne = new Catalog.Person { FaceCount = 1 };
        var strangerTwo = new Catalog.Person { FaceCount = 1 };
        db.Faces.Add(Face(named));
        var one = Face(strangerOne);
        one.Box = [0.5, 0.5, 0.2, 0.2];
        db.Faces.Add(one);
        var two = Face(strangerTwo);
        two.AssetId = Ids.Of(2);
        db.Faces.Add(two);
        await db.SaveChangesAsync();
        var contexts = new TestContexts(catalog.ConnectionStringFor(db));
        var library = new LibraryQueries(contexts, TimeProvider.System, user: new SignedIn(Ana));

        var detail = await library.GetAssetAsync(Ids.Of(1));
        var preview = await new FaceSettings(contexts, TimeProvider.System).PreviewUnnamedWithFewFacesAsync(12);

        var unnamed = Assert.Single(detail!.People, p => p.Name is null);
        Assert.Equal((Ids.Of(1), 256, 192), (unnamed.Crop!.AssetId, unnamed.Crop.ThumbnailWidth, unnamed.Crop.ThumbnailHeight));
        Assert.Equal([0.5, 0.5, 0.2, 0.2], unnamed.Crop.Box);
        Assert.Equal(one.Id, unnamed.FaceId);
        Assert.Equal(2, preview.People);
        Assert.Equal([Ids.Of(1), Ids.Of(2)], preview.Crops.Select(c => c.AssetId));
        // Only the first asked for are cropped, and the count is of all.
        var one1 = await new FaceSettings(contexts, TimeProvider.System).PreviewUnnamedWithFewFacesAsync(1);
        Assert.Equal((2, 1), (one1.People, one1.Crops.Count));
    }

    [Fact]
    public async Task Previews_each_persons_best_scoring_face_and_skips_a_malformed_box_and_a_thumbnail_of_no_size()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(1), new DateTime(2019, 7, 14)));
        db.Assets.Add(CatalogData.Photo(Ids.Of(2), new DateTime(2019, 7, 15)));
        db.Assets.Add(CatalogData.Photo(Ids.Of(3), new DateTime(2019, 7, 16)));
        var flat = CatalogData.Photo(Ids.Of(4), new DateTime(2019, 7, 17));
        flat.Derivatives = [new Derivative { AssetId = Ids.Of(4), Name = "thumb-256", BlobPath = $"{Ids.Of(4)}/thumb-256.webp", Width = 0, Height = 0, ContentType = "image/webp" }];
        db.Assets.Add(flat);
        var two = new Catalog.Person { FaceCount = 2 };
        var short_ = new Catalog.Person { FaceCount = 1 };
        var noSize = new Catalog.Person { FaceCount = 1 };
        Face OnAsset(int n, Catalog.Person person, double score, double[]? box = null)
        {
            var face = Face(person);
            face.AssetId = Ids.Of(n);
            face.Score = score;
            face.Box = box ?? [0.1, 0.2, 0.3, 0.4];
            return face;
        }

        db.Faces.AddRange(OnAsset(1, two, 0.5), OnAsset(2, two, 0.9), OnAsset(3, short_, 0.9, [0.1, 0.2, 0.3]), OnAsset(4, noSize, 0.9));
        await db.SaveChangesAsync();
        var contexts = new TestContexts(catalog.ConnectionStringFor(db));

        var preview = await new FaceSettings(contexts, TimeProvider.System).PreviewUnnamedWithFewFacesAsync(12);

        // Three people are counted; only the one with a usable face and thumbnail is shown, by its best face.
        Assert.Equal(3, preview.People);
        Assert.Equal([Ids.Of(2)], preview.Crops.Select(c => c.AssetId));
    }

    [Fact]
    public async Task Lists_a_person_once_in_a_photo_and_gives_a_face_with_a_short_box_no_crop()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(1), new DateTime(2019, 7, 14)));
        var twice = new Catalog.Person { FaceCount = 2 };
        var broken = new Catalog.Person { FaceCount = 1 };
        var first = Face(twice);
        var second = Face(twice);
        second.Box = [0.6, 0.6, 0.2, 0.2];
        var shortBox = Face(broken);
        shortBox.Box = [0.1, 0.2];
        db.Faces.AddRange(first, second, shortBox);
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var people = (await queries.GetAssetAsync(Ids.Of(1)))!.People;

        Assert.Equal(2, people.Count);
        var listed = Assert.Single(people, p => p.Id == twice.Id);
        Assert.Equal(first.Id, listed.FaceId);
        Assert.NotNull(listed.Crop);
        Assert.Null(Assert.Single(people, p => p.Id == broken.Id).Crop);
    }

    private static Face Face(Catalog.Person person)
    {
        var embedding = new float[128];
        embedding[0] = 1;
        return new Face
        {
            AssetId = Ids.Of(1),
            Box = [0.1, 0.2, 0.3, 0.4],
            Score = 0.9,
            Embedding = new Vector(embedding),
            Person = person,
            Assignment = FaceAssignments.Auto,
        };
    }

    private sealed class SignedIn(string email) : ICurrentUser
    {
        public string? Email => email;
    }

    [Fact]
    public async Task Turning_recognition_off_from_the_page_empties_faces_and_people_and_keeps_the_photos()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(1), new DateTime(2019, 7, 14)));
        var person = new Catalog.Person { Name = "Ana", UserEmail = Ana, FaceCount = 1 };
        var embedding = new float[128];
        embedding[0] = 1;
        db.Faces.Add(new Face
        {
            AssetId = Ids.Of(1),
            Box = [0.1, 0.2, 0.3, 0.4],
            Score = 0.9,
            Embedding = new Vector(embedding),
            Person = person,
            Assignment = FaceAssignments.Auto,
        });
        await db.SaveChangesAsync();
        await using var web = new WebFactory(catalog.ConnectionStringFor(db)) { AccessEmails = Ana };
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);
        using var response = await FaceSettingsPostTests.PostAsync(http, "/settings?confirm=faces-off", "off", new() { ["Off"] = "yes" });

        Assert.Equal("/settings?note=faces-off", response.Headers.Location?.PathAndQuery);
        db.ChangeTracker.Clear();
        Assert.Equal((0, 0, 1), (await db.Faces.CountAsync(), await db.People.CountAsync(), await db.Assets.CountAsync()));
        var deletion = await db.FaceDataDeletions.SingleAsync();
        Assert.Equal((Ana, 1, 1), (deletion.DeletedBy, deletion.Faces, deletion.People));
        var settings = WebUtility.HtmlDecode(await http.GetStringAsync(new Uri("/settings", UriKind.Relative)));
        Assert.Contains("Face recognition is off: no face is imported.", settings, StringComparison.Ordinal);
        Assert.Contains($"by {Ana}: recognition turned off; faces deleted: 1, people deleted: 1.", settings, StringComparison.Ordinal);
    }

}
