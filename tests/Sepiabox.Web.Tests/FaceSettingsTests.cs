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
        Assert.StartsWith("O reconhecimento facial está ligado", page.Find(".faces-status").TextContent, StringComparison.Ordinal);
        Assert.Equal(("get", "settings"), (page.Find("form.ask-off").GetAttribute("method"), page.Find("form.ask-off").GetAttribute("action")));
        Assert.Equal("Desligar e apagar os dados faciais", page.Find("form.ask-off button").TextContent);
        Assert.Empty(page.FindAll("form.confirm"));

        Services.GetRequiredService<NavigationManager>().NavigateTo("settings?confirm=faces-off");
        var confirming = Render<Settings>();

        Assert.StartsWith("Desligar o reconhecimento facial? Todos os rostos e todas as pessoas", confirming.Find(".confirm-off .note").TextContent, StringComparison.Ordinal);
        Assert.Equal("post", confirming.Find("form.confirm").GetAttribute("method"));
        Assert.Equal("Sim, desligar e apagar", confirming.Find("form.confirm button").TextContent);
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
                "20/10/2026 08:00, por ana@example.com: reconhecimento desligado; rostos apagados: 120, pessoas apagadas: 9.",
            ],
            page.FindAll(".face-deletions li").Select(li => li.TextContent));
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
