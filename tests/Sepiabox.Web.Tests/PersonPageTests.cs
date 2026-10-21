using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Catalog.People;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>A person's page (S-020): its forms and notes (bUnit). Synthetic data and example.com addresses only.</summary>
public sealed class PersonPageFormTests : LocalizedContext
{
    private readonly FakeLibrary _library = new();

    public PersonPageFormTests()
    {
        Services.AddSingleton<ILibraryQueries>(_library);
        Services.AddSingleton<IPeopleEditor>(new FakePeopleEditor());
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: null);
    }

    [Fact]
    public void Offers_rename_this_is_me_merge_and_the_face_corrections()
    {
        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal("Ana", page.Find("h1").TextContent);
        Assert.Equal("Ana", page.Find("form.rename input[name=Name]").GetAttribute("value"));
        Assert.Equal("This is me", page.Find("form.me button").TextContent);
        // Everyone else can be merged in, not the person themselves.
        Assert.Equal(["8", "9"], page.FindAll("form.merge option").Select(o => o.GetAttribute("value")));
        Assert.Equal(
            ["not-this-person:70", "not-a-face:70", "not-this-person:71", "not-a-face:71"],
            page.FindAll(".person-faces button").Select(b => b.GetAttribute("value")));
    }

    [Fact]
    public void Says_sou_eu_and_sem_nome_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        _library.Persons[7] = PersonPages.Person(7, name: null, user: null);

        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal("Sem nome", page.Find("h1").TextContent);
        Assert.Equal("Sou eu", page.Find("form.me button").TextContent);
        Assert.Equal(["Não é esta pessoa", "Não é um rosto"], page.FindAll(".person-faces li:first-child button").Select(b => b.TextContent));
    }

    [Fact]
    public void Shows_who_said_this_is_me_and_offers_no_button_to_someone_else()
    {
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: "ana@example.com");

        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal("Linked to ana@example.com.", page.Find("form.me span").TextContent);
        Assert.Empty(page.FindAll("form.me button"));
    }

    [Theory]
    [InlineData("NameTaken", "Another person already has this name.")]
    [InlineData("ClaimedByAnother", "Someone else already said this person is them.")]
    [InlineData("NameTooLong", "The name is too long: at most 200 characters.")]
    [InlineData("merged", "The two people are now one.")]
    public void Shows_what_happened_from_the_note_in_the_address(string note, string expected)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"people/7?note={note}");

        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal(expected, page.Find(".note").TextContent);
    }

    [Fact]
    public void Shows_nothing_for_a_note_it_does_not_know()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("people/7?note=%3Cscript%3E");

        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Empty(page.FindAll(".note"));
    }
}

/// <summary>The page's forms over HTTP, signed in, with an in-memory library and a recording editor.</summary>
public sealed partial class PersonPagePostTests
{
    private const string Ana = "ana@example.com";

    [Fact]
    public async Task Renames_and_redirects_back_with_a_note()
    {
        var (web, editor) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await PostAsync(http, "rename", new() { ["Name"] = "Ana" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/people/7?note=renamed", response.Headers.Location?.PathAndQuery);
        Assert.Equal(["rename 7 Ana"], editor.Calls);
    }

    [Fact]
    public async Task Says_this_is_me_for_the_signed_in_user_and_shows_a_rejection()
    {
        var (web, editor) = Web(CurationOutcome.ClaimedByAnother);
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await PostAsync(http, "me", new() { ["Me"] = "link" });

        Assert.Equal("/people/7?note=ClaimedByAnother", response.Headers.Location?.PathAndQuery);
        Assert.Equal([$"link 7 {Ana}"], editor.Calls);
    }

    [Theory]
    [InlineData("faces", "Face", "not-this-person:70", "not-this-person 7 70")]
    [InlineData("faces", "Face", "not-a-face:71", "not-a-face 7 71")]
    [InlineData("merge", "Other", "9", "merge 7 9")]
    public async Task Sends_each_correction_to_the_editor(string form, string field, string value, string call)
    {
        var (web, editor) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await PostAsync(http, form, new() { [field] = value });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal([call], editor.Calls);
    }

    [Fact]
    public async Task Answers_404_for_a_person_who_is_not_there()
    {
        var (web, _) = Web();
        await using var __ = web;
        using var http = web.CreateSignedInClient(Ana);

        using var response = await http.GetAsync(new Uri("/people/12345", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static (WebFactory Web, FakePeopleEditor Editor) Web(CurationOutcome outcome = CurationOutcome.Done)
    {
        var library = new FakeLibrary();
        library.Persons[7] = PersonPages.Person(7, name: null, user: null);
        var editor = new FakePeopleEditor { Outcome = outcome };
        return (new WebFactory(library) { AccessEmails = Ana, PeopleEditor = editor }, editor);
    }

    // As a browser: the page's antiforgery token, then the form with it.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient http, string form, Dictionary<string, string> fields)
    {
        var page = await http.GetStringAsync(new Uri("/people/7", UriKind.Relative));
        var match = TokenField().Match(page);
        Assert.True(match.Success, page[..Math.Min(page.Length, 4000)]);
        var token = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        fields["_handler"] = form;
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
        return await http.PostAsync(new Uri("/people/7", UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"|type=\"hidden\" name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenField();
}

internal static class PersonPages
{
    public static PersonDetail Person(long id, string? name, string? user) => new(
        id,
        name,
        user,
        2,
        [new PersonFace(70, Ids.Of(1), new FaceCrop(Ids.Of(1), [0.1, 0.1, 0.2, 0.2], 256, 192)), new PersonFace(71, Ids.Of(2), null)],
        [new PersonSummary(8, "Bruno", 3, null), new PersonSummary(9, null, 1, null)]);
}
