using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Catalog.People;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Components.People;
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
    public void Uses_the_shared_parts_the_head_with_the_way_back_a_panel_for_the_corrections_and_counted_section_titles()
    {
        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal("people", page.Find("p.back a").GetAttribute("href"));
        Assert.Equal("Ana", page.Find("div.page-head h1.page-heading").TextContent);
        Assert.NotEmpty(page.FindAll("div.page-head .page-sub"));
        Assert.NotEmpty(page.FindAll("section.panel.block.person-edit"));
        var counts = page.FindAll(".section-title .n").Select(n => n.TextContent).ToList();
        Assert.Equal(["2", "2"], counts);
    }

    [Fact]
    public void Offers_rename_this_is_me_merge_and_the_face_corrections()
    {
        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal("Ana", page.Find("h1").TextContent);
        Assert.Equal("Ana", page.Find("form.rename input[name=Name]").GetAttribute("value"));
        Assert.Equal("This is me", page.Find("form.me button").TextContent);
        // Everyone else can be merged in, not the person themselves; the GET form only opens the confirmation.
        Assert.Equal(["8", "9"], page.FindAll("form.merge option").Select(o => o.GetAttribute("value")));
        Assert.Equal(("get", "people/7/merge"), (page.Find("form.merge").GetAttribute("method"), page.Find("form.merge").GetAttribute("action")));
        Assert.Equal(
            ["not-this-person:70", "not-a-face:70", "not-this-person:71", "not-a-face:71", "give-back:72", "give-back:73"],
            page.FindAll(".person-faces button").Select(b => b.GetAttribute("value")));
        Assert.Equal(
            ["people/7/faces/70/move", "people/7/faces/71/move", "people/7/faces/72/move", "people/7/faces/73/move"],
            page.FindAll(".person-faces a.small").Select(a => a.GetAttribute("href")));
        Assert.Equal("Excluded faces", page.FindAll("h2")[1].TextContent);
        // Only the face marked "Not a face" says so.
        Assert.Equal("(not a face)", Assert.Single(page.FindAll(".excluded-faces .not-a-face")).TextContent);
    }

    [Fact]
    public void Gives_every_face_card_an_id_to_come_back_to()
    {
        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal(["face-70", "face-71", "face-72", "face-73"], page.FindAll(".person-faces li").Select(li => li.GetAttribute("id")));
    }

    [Theory]
    [InlineData(70L, "face-71")]
    [InlineData(71L, "face-70")]
    [InlineData(72L, "face-73")]
    [InlineData(73L, "face-72")]
    [InlineData(999L, null)]
    public void Lands_on_the_next_card_of_the_list_else_the_one_before(long faceId, string? expected) =>
        Assert.Equal(expected, PersonLinks.AnchorAfter(PersonPages.Person(7, "Ana", null), faceId));

    [Theory]
    [InlineData(81L, "face-82")]
    [InlineData(82L, "face-81")]
    public void Lands_on_the_next_card_when_the_face_has_one_on_each_side(long faceId, string expected)
    {
        var three = PersonPages.Person(7, "Ana", null) with
        {
            Faces = [new PersonFace(80, Ids.Of(1), null), new PersonFace(81, Ids.Of(2), null), new PersonFace(82, Ids.Of(3), null)],
            ExcludedFaces = [],
        };

        Assert.Equal(expected, PersonLinks.AnchorAfter(three, faceId));
    }

    [Fact]
    public void Lands_nowhere_in_particular_after_the_only_face_of_a_list_and_for_no_person()
    {
        var alone = PersonPages.Person(7, "Ana", null) with { Faces = [new PersonFace(70, Ids.Of(1), null)], ExcludedFaces = [] };

        Assert.Null(PersonLinks.AnchorAfter(alone, 70));
        Assert.Null(PersonLinks.AnchorAfter(null, 70));
    }

    [Fact]
    public void Says_sou_eu_and_sem_nome_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        _library.Persons[7] = PersonPages.Person(7, name: null, user: null);

        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal("Sem nome", page.Find("h1").TextContent);
        Assert.Equal("Sou eu", page.Find("form.me button").TextContent);
        Assert.Equal(["Não é esta pessoa", "Não é um rosto"], page.FindAll(".person-faces:not(.excluded-faces) li:first-child button").Select(b => b.TextContent));
    }

    [Fact]
    public void Lays_the_corrections_out_as_labelled_rows_of_one_card_with_the_removals_last_in_the_danger_style()
    {
        _library.Persons[7] = PersonPages.Person(7, name: null, user: null);

        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        var card = page.Find("section.person-edit");
        var rows = card.QuerySelectorAll(".form-row").ToList();
        // Rename, This is me, Merge; then Delete and Ignore (an unnamed person), each with a label, a field and an action.
        Assert.Equal(5, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.NotNull(row.QuerySelector(".form-label"));
            Assert.NotNull(row.QuerySelector(".field"));
            Assert.NotNull(row.QuerySelector(".form-actions"));
        });
        Assert.Equal(["Name", "This is me", "Same person as"], rows.Take(3).Select(r => r.QuerySelector(".form-label")!.TextContent));
        // The merge row explains itself under the field.
        Assert.Equal("You see both people before anything changes.", rows[2].QuerySelector(".field .hint")!.TextContent);
        // The removals are below, inside the danger zone, as danger buttons that only open a confirmation.
        var zone = card.QuerySelector(".danger-zone")!;
        Assert.Equal(["people/7/delete", "people/7/ignore"], zone.QuerySelectorAll("a.button.danger").Select(a => a.GetAttribute("href")));
        Assert.All(zone.QuerySelectorAll("a.button.danger"), a => Assert.Equal("false", a.GetAttribute("data-enhance-nav")));
        Assert.Empty(card.QuerySelectorAll("form button.danger"));
    }

    [Fact]
    public void Says_this_is_me_without_assuming_a_gender_in_portuguese_and_names_the_person_on_the_move_page()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: null);

        var page = Render<Person>(p => p.Add(x => x.Id, 7L));
        Services.GetRequiredService<NavigationManager>().NavigateTo("people/7/faces/70/move");
        var move = Render<FaceMove>(p => p.Add(x => x.Id, 7L).Add(x => x.FaceId, 70L));

        Assert.Equal("Sou eu", page.Find("form.me .form-label").TextContent);
        Assert.Equal("Mover um rosto de Ana", move.Find("h1").TextContent);
        Assert.Equal("Mover este rosto para", move.Find("label.form-label").TextContent);
    }

    [Fact]
    public void Offers_no_ignore_for_a_named_person_and_styles_the_face_buttons_as_secondary()
    {
        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Single(page.FindAll(".danger-zone a.button.danger"));
        Assert.All(page.FindAll(".person-faces button"), b => Assert.Contains("secondary", b.ClassName, StringComparison.Ordinal));
    }

    [Fact]
    public void Shows_who_said_this_is_me_and_offers_no_button_to_someone_else()
    {
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: "ana@example.com");

        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal("Linked to ana@example.com.", page.Find("form.me .field span").TextContent);
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

    [Theory]
    [InlineData("NameTaken", "error", "alert")]
    [InlineData("DifferentUsers", "error", "alert")]
    [InlineData("renamed", "success", "status")]
    [InlineData("merged", "success", "status")]
    public void Draws_a_refusal_as_an_error_that_is_announced_at_once_and_a_done_as_a_success(string note, string variant, string role)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"people/7?note={note}");

        var page = Render<Person>(p => p.Add(x => x.Id, 7L));

        Assert.Equal(role, page.Find($"p.note.note-{variant}").GetAttribute("role"));
    }

    [Fact]
    public void Shows_both_people_and_which_one_stays_before_a_merge_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        _library.Persons[8] = PersonPages.Person(8, name: "Bruno", user: null);
        Services.GetRequiredService<NavigationManager>().NavigateTo("people/7/merge?other=8");

        var page = Render<PersonMerge>(p => p.Add(x => x.Id, 7L));

        Assert.Equal(["Será apagada", "Fica"], page.FindAll(".merge-people .caption").Select(c => c.TextContent));
        Assert.Equal(["Bruno", "Ana"], page.FindAll(".merge-people .name").Select(c => c.TextContent));
        Assert.Equal("Depois de juntar: Ana (2 rostos) fica com os rostos de Bruno (2 rostos), e Bruno deixa de existir.", page.Find(".note:not(.danger)").TextContent);
        Assert.Equal("Juntar em Ana", page.Find("form.confirm button").TextContent);
    }

    [Fact]
    public void Refers_to_the_cards_when_a_person_to_merge_is_unnamed()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        _library.Persons[9] = PersonPages.Person(9, name: null, user: null);
        Services.GetRequiredService<NavigationManager>().NavigateTo("people/7/merge?other=9");

        var page = Render<PersonMerge>(p => p.Add(x => x.Id, 7L));

        // Left goes, right stays, as the sentence says.
        Assert.Equal(["Será apagada", "Fica"], page.FindAll(".merge-people .caption").Select(c => c.TextContent));
        Assert.Equal(["Sem nome", "Ana"], page.FindAll(".merge-people .name").Select(c => c.TextContent));
        Assert.Equal("Depois de juntar: Ana (2 rostos) fica com os rostos de Sem nome (2 rostos), e Sem nome deixa de existir.", page.Find(".note").TextContent);
    }

    private IRenderedComponent<PersonMerge> MergePage(string query)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("people/7/merge" + query);
        return Render<PersonMerge>(p => p.Add(x => x.Id, 7L));
    }

    [Fact]
    public void Keeps_the_named_person_when_the_other_is_unnamed_whichever_page_the_merge_started_from()
    {
        // Ana (named, 100 faces) with an unnamed person of 1,500: the named one stays, from either side.
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: null) with { FaceCount = 100 };
        _library.Persons[9] = PersonPages.Person(9, name: null, user: null) with { FaceCount = 1500 };
        var fromAna = MergePage("?other=9");
        Assert.Equal(["Unnamed", "Ana"], fromAna.FindAll(".merge-people .name").Select(c => c.TextContent));

        // Started from the unnamed person's page: the same cards, and the same direction.
        _library.Persons[9] = PersonPages.Person(9, name: null, user: null) with { FaceCount = 1500 };
        Services.GetRequiredService<NavigationManager>().NavigateTo("people/9/merge?other=7");
        var fromUnnamed = Render<PersonMerge>(p => p.Add(x => x.Id, 9L));

        Assert.Equal(["Unnamed", "Ana"], fromUnnamed.FindAll(".merge-people .name").Select(c => c.TextContent));
        Assert.Empty(fromUnnamed.FindAll(".note.danger"));
        Assert.DoesNotContain("danger", fromUnnamed.Find("form.confirm button").ClassName, StringComparison.Ordinal);
        Assert.Equal("Merge into Ana", fromUnnamed.Find("form.confirm button").TextContent);
    }

    [Fact]
    public void Keeps_the_person_with_more_faces_when_both_or_neither_have_a_name()
    {
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: null) with { FaceCount = 40 };
        _library.Persons[8] = PersonPages.Person(8, name: "Bruno", user: null) with { FaceCount = 300 };

        var page = MergePage("?other=8");

        Assert.Equal(["Ana", "Bruno"], page.FindAll(".merge-people .name").Select(c => c.TextContent));
    }

    [Fact]
    public void Keeps_the_person_the_user_said_is_them_even_against_a_named_one_with_more_faces()
    {
        _library.Persons[7] = PersonPages.Person(7, name: null, user: "ana@example.com") with { FaceCount = 10 };
        _library.Persons[8] = PersonPages.Person(8, name: "Bruno", user: null) with { FaceCount = 900 };

        var page = MergePage("?other=8");

        Assert.Equal(["Bruno", "Unnamed"], page.FindAll(".merge-people .name").Select(c => c.TextContent));
    }

    [Fact]
    public void States_the_result_with_names_and_counts_and_offers_a_swap_that_reverses_it_and_warns_when_a_named_person_would_go()
    {
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: null) with { FaceCount = 100 };
        _library.Persons[9] = PersonPages.Person(9, name: null, user: null) with { FaceCount = 1500 };

        var page = MergePage("?other=9");

        Assert.Equal("After merging: Ana (100 faces) keeps the faces of Unnamed (1500 faces), and Unnamed no longer exists.", page.Find(".note").TextContent);
        Assert.Equal("people/7/merge?other=9&swap=1", page.Find("a.merge-swap").GetAttribute("href"));
        Assert.Equal("0", page.Find("input[name=Swap]").GetAttribute("value"));

        var swapped = MergePage("?other=9&swap=1");

        Assert.Equal(["Ana", "Unnamed"], swapped.FindAll(".merge-people .name").Select(c => c.TextContent));
        Assert.Equal("1", swapped.Find("input[name=Swap]").GetAttribute("value"));
        Assert.Equal("people/7/merge?other=9", swapped.Find("a.merge-swap").GetAttribute("href"));
        Assert.Contains("Ana has a name and will be deleted", swapped.Find(".note.danger").TextContent, StringComparison.Ordinal);
        Assert.Equal("Ana", swapped.Find(".note.danger strong").TextContent);
        // The danger comes before the result sentence, the button is the danger one and names who stays, and Cancel is a secondary button before it.
        Assert.Equal(["note note-error danger", "note"], swapped.FindAll("p.note").Select(n => n.ClassName));
        Assert.Equal("button danger", swapped.Find("form.confirm button").ClassName);
        Assert.Equal("Merge into Unnamed", swapped.Find("form.confirm button").TextContent);
        Assert.Equal("a", swapped.Find("form.confirm").Children[^2].LocalName);
        Assert.Contains("secondary", swapped.Find("form.confirm").Children[^2].ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Marks_the_person_who_goes_as_deleted_and_the_one_who_stays_with_an_arrow_between()
    {
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: null) with { FaceCount = 100 };
        _library.Persons[9] = PersonPages.Person(9, name: null, user: null) with { FaceCount = 1500 };

        var page = MergePage("?other=9");

        Assert.Equal(["Will be deleted", "Stays"], page.FindAll(".merge-people .merge-badge").Select(b => b.TextContent));
        Assert.Contains("merge-leaves", page.Find(".merge-people li:first-child").ClassName, StringComparison.Ordinal);
        Assert.Contains("merge-stays", page.Find(".merge-people li:last-child").ClassName, StringComparison.Ordinal);
        Assert.Equal("true", page.Find(".merge-arrow").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Says_that_the_person_who_goes_is_linked_to_the_user_when_they_are_unnamed()
    {
        _library.Persons[7] = PersonPages.Person(7, name: null, user: "ana@example.com") with { FaceCount = 5 };
        _library.Persons[8] = PersonPages.Person(8, name: "Bruno", user: null) with { FaceCount = 900 };

        var page = MergePage("?other=8&swap=1");

        Assert.Equal("Unnamed is linked to you (\"This is me\") and will be deleted for good. Go on only if that is what you want; otherwise use Swap.", page.Find(".note.danger").TextContent.Trim());
    }

    [Fact]
    public void Says_the_result_and_the_swap_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        _library.Persons[8] = PersonPages.Person(8, name: "Bruno", user: null) with { FaceCount = 3 };

        var page = MergePage("?other=8&swap=1");

        Assert.Equal("Trocar: manter a outra pessoa", page.Find("a.merge-swap").TextContent);
        Assert.Contains("tem nome e será apagada", page.Find(".note.danger").TextContent, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("not-this-person:70", "#face-71")]
    [InlineData("not-a-face:71", "#face-70")]
    [InlineData("give-back:72", "#face-73")]
    [InlineData("give-back:73", "#face-72")]
    public async Task Sends_the_person_back_to_the_next_face_so_the_page_keeps_its_place(string value, string fragment)
    {
        var (web, _) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await PostAsync(http, "faces", new() { ["Face"] = value });

        Assert.Equal(fragment, response.Headers.Location?.Fragment);
        Assert.StartsWith("/people/7?note=", response.Headers.Location?.PathAndQuery, StringComparison.Ordinal);
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
    [InlineData("faces", "Face", "give-back:72", "return 7 72")]
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
    public async Task Merges_only_from_the_confirmation_page()
    {
        var (web, editor) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        var confirmation = await http.GetStringAsync(new Uri("/people/7/merge?other=9", UriKind.Relative));
        Assert.Empty(editor.Calls);
        Assert.Contains("keeps the faces of", WebUtility.HtmlDecode(confirmation), StringComparison.Ordinal);
        using var response = await PostAsync(http, "/people/7/merge?other=9", "confirm", new() { ["Confirmed"] = "yes" });

        Assert.Equal("/people/7?note=merged", response.Headers.Location?.PathAndQuery);
        Assert.Equal(["merge 7 9"], editor.Calls);
    }

    [Theory]
    [InlineData("1", "merge 7 9 swap")]
    [InlineData("0", "merge 7 9")]
    [InlineData(null, "merge 7 9")]
    public async Task Passes_a_swap_to_the_editor_only_when_the_form_says_so(string? swap, string call)
    {
        var (web, editor) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);
        var fields = new Dictionary<string, string> { ["Confirmed"] = "yes" };
        if (swap is not null)
        {
            fields["Swap"] = swap;
        }

        using var response = await PostAsync(http, "/people/7/merge?other=9", "confirm", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal([call], editor.Calls);
    }

    [Theory]
    [InlineData("9", "move 7 70 9")]
    [InlineData("new", "move 7 70 new")]
    public async Task Moves_a_face_from_its_page(string target, string call)
    {
        var (web, editor) = Web(CurationOutcome.PhotoTaken);
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await PostAsync(http, "/people/7/faces/70/move", "move", new() { ["Target"] = target });

        Assert.Equal("/people/7?note=PhotoTaken", response.Headers.Location?.PathAndQuery);
        Assert.Equal("#face-71", response.Headers.Location?.Fragment);
        Assert.Equal([call], editor.Calls);
    }

    [Theory]
    [InlineData("/people/7/merge?other=12345")]
    [InlineData("/people/7/faces/12345/move")]
    public async Task Answers_404_for_a_merge_or_a_move_of_something_not_there(string address)
    {
        var (web, _) = Web();
        await using var __ = web;
        using var http = web.CreateSignedInClient(Ana);

        using var response = await http.GetAsync(new Uri(address, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        library.Persons[9] = PersonPages.Person(9, name: null, user: null);
        var editor = new FakePeopleEditor { Outcome = outcome };
        return (new WebFactory(library) { AccessEmails = Ana, PeopleEditor = editor }, editor);
    }

    // As a browser: the page's antiforgery token, then the form with it.
    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string form, Dictionary<string, string> fields) =>
        PostAsync(http, "/people/7", form, fields);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient http, string address, string form, Dictionary<string, string> fields)
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

internal static class PersonPages
{
    public static PersonDetail Person(long id, string? name, string? user) => new(
        id,
        name,
        user,
        2,
        [new PersonFace(70, Ids.Of(1), new FaceCrop(Ids.Of(1), [0.1, 0.1, 0.2, 0.2], 256, 192)), new PersonFace(71, Ids.Of(2), null)],
        [new PersonSummary(8, "Bruno", 3, null), new PersonSummary(9, null, 1, null)],
        [new PersonFace(72, Ids.Of(3), null), new PersonFace(73, Ids.Of(4), null, NotAFace: true)]);
}
