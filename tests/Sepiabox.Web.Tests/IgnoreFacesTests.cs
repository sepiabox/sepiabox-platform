using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>Ignoring the faces of strangers (#107, D-117): links, confirmations and what they send (bUnit). Synthetic data only.</summary>
public sealed class IgnoreFacesPageTests : LocalizedContext
{
    private readonly FakeFaceSettings _faces = new();
    private readonly FakeLibrary _library = new();

    public IgnoreFacesPageTests()
    {
        Services.AddSingleton<IFaceSettings>(_faces);
        Services.AddSingleton<ILibraryQueries>(_library);
        Services.AddSingleton<IPeopleEditor>(new FakePeopleEditor());
    }

    private AssetDetail PhotoWith(params AssetPerson[] people)
    {
        var asset = Details.Photo(Ids.Of(1), new DateTime(2019, 7, 14, 10, 32, 0)) with { };
        asset = asset with { People = people };
        _library.Assets[asset.Id] = asset;
        return asset;
    }

    private AssetDetail PhotoWithFaces(params AssetFace[] faces)
    {
        var asset = Details.Photo(Ids.Of(1), new DateTime(2019, 7, 14, 10, 32, 0)) with { };
        asset = asset with { Faces = faces, People = [.. faces.Select(f => new AssetPerson(f.PersonId, f.Name, f.FaceId))] };
        _library.Assets[asset.Id] = asset;
        return asset;
    }

    [Fact]
    public void Gives_each_unnamed_face_its_own_ignore_link_in_its_row_with_the_right_face_and_never_a_named_one()
    {
        var asset = PhotoWithFaces(new AssetFace(10, 1, "Ana", null), new AssetFace(20, 2, null, null), new AssetFace(30, 3, null, null));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}");

        var page = Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));

        var rows = page.FindAll(".people-card li.face-row");
        Assert.Equal(3, rows.Count);
        // Ana's row has none; each stranger's has one, to its own face, beside its own "Add a name" link.
        Assert.Empty(rows[0].QuerySelectorAll("a.face-ignore"));
        Assert.Equal($"photos/{asset.Id}/ignore-faces?face=20", rows[1].QuerySelector("a.face-ignore")!.GetAttribute("href"));
        Assert.Equal($"photos/{asset.Id}/ignore-faces?face=30", rows[2].QuerySelector("a.face-ignore")!.GetAttribute("href"));
        Assert.Equal("people/2", rows[1].QuerySelector("a.face")!.GetAttribute("href"));
        Assert.Equal("people/3", rows[2].QuerySelector("a.face")!.GetAttribute("href"));
        Assert.Equal("Ignore and delete", rows[1].QuerySelector("a.face-ignore")!.TextContent);
        // Both labels of a stranger's row carry the same number, from one counter.
        Assert.Equal("Add a name, unnamed face 1", rows[1].QuerySelector("a.face")!.GetAttribute("aria-label"));
        Assert.Equal("Ignore and delete unnamed face 1", rows[1].QuerySelector("a.face-ignore")!.GetAttribute("aria-label"));
        Assert.Equal("Add a name, unnamed face 2", rows[2].QuerySelector("a.face")!.GetAttribute("aria-label"));
        Assert.Equal("Ignore and delete unnamed face 2", rows[2].QuerySelector("a.face-ignore")!.GetAttribute("aria-label"));
        Assert.Equal($"photos/{asset.Id}/ignore-faces", page.Find("p.ignore-faces a").GetAttribute("href"));
        Assert.Equal("Ignore and delete the unnamed faces in this photo", page.Find("p.ignore-faces a").TextContent);
        // Enhanced navigation would keep the focus where it was, not on the way out of the confirmation.
        Assert.All(page.FindAll("a.face-ignore, p.ignore-faces a"), a => Assert.Equal("false", a.GetAttribute("data-enhance-nav")));
        Assert.Empty(page.FindAll(".ignore-card"));
    }

    [Fact]
    public void Says_the_ignore_links_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        var asset = PhotoWithFaces(new AssetFace(20, 2, null, null), new AssetFace(30, 3, null, null));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}");

        var page = Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));

        var link = page.FindAll("a.face-ignore")[1];
        Assert.Equal("Ignorar e apagar", link.TextContent);
        Assert.Equal("Ignorar e apagar o rosto sem nome 2", link.GetAttribute("aria-label"));
    }

    [Fact]
    public void Offers_only_the_row_s_link_for_a_photo_with_one_unnamed_face_and_none_for_named_people()
    {
        var one = PhotoWithFaces(new AssetFace(10, 1, "Ana", null), new AssetFace(20, 2, null, null));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{one.Id}");

        var page = Render<PhotoDetail>(p => p.Add(d => d.Id, one.Id));

        Assert.Single(page.FindAll("a.face-ignore"));
        Assert.Empty(page.FindAll("p.ignore-faces"));
    }

    [Fact]
    public void Says_what_happened_from_the_note_in_the_address()
    {
        var asset = PhotoWith(new AssetPerson(2, null, 20));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}?note=ignored");

        var page = Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));

        Assert.Equal("The faces were ignored.", page.Find(".note").TextContent);
    }

    [Fact]
    public void Asks_before_ignoring_one_face_and_says_what_it_does_not_reach()
    {
        var crop = new FaceCrop(Ids.Of(1), [0.1, 0.1, 0.2, 0.2], 256, 192);
        var asset = PhotoWithFaces(new AssetFace(10, 1, "Ana", null), new AssetFace(20, 2, null, crop), new AssetFace(30, 3, null, crop));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}/ignore-faces?face=20");

        var page = Render<PhotoIgnoreFaces>(p => p.Add(d => d.Id, asset.Id));

        Assert.Equal("Ignore and delete faces", page.Find("h1").TextContent);
        Assert.Single(page.FindAll("p.ignore-warning"));
        Assert.Equal("This cannot be undone. The photos stay.", page.Find("p.ignore-warning strong").TextContent);
        Assert.Contains("1 unnamed face in this photo will be ignored.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("deleted for good", page.Find("ul.ignore-effects").TextContent, StringComparison.Ordinal);
        Assert.Contains("People with a name are never touched", page.Find("ul.ignore-effects").TextContent, StringComparison.Ordinal);
        Assert.Equal("What about the cloud copies?", page.Find("details.ignore-cloud summary").TextContent);
        // The face that goes is shown cropped, not the whole photo.
        Assert.Single(page.FindAll("ul.ignore-crops li"));
        Assert.Empty(page.FindAll("figure.preview"));
        // The way out comes first (and has the focus); the destructive button is the filled one.
        var actions = page.FindAll("form.confirm > a, form.confirm > button");
        Assert.Equal(["a", "button"], actions.Select(a => a.LocalName));
        Assert.True(actions[0].HasAttribute("autofocus"));
        Assert.Contains("secondary", actions[0].ClassName, StringComparison.Ordinal);
        Assert.Equal("Delete for good", actions[1].TextContent);
        Assert.Contains("danger", actions[1].ClassName, StringComparison.Ordinal);
        Assert.Empty(_faces.Calls);
    }

    [Fact]
    public void Counts_every_unnamed_face_when_none_is_named_and_says_so_in_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        var asset = PhotoWithFaces(new AssetFace(20, 2, null, null), new AssetFace(30, 3, null, null));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}/ignore-faces");

        var page = Render<PhotoIgnoreFaces>(p => p.Add(d => d.Id, asset.Id));

        Assert.Equal("Ignorar e apagar rostos", page.Find("h1").TextContent);
        Assert.Equal("Isso não pode ser desfeito. As fotos ficam.", page.Find("p.ignore-warning strong").TextContent);
        Assert.Contains("2 rostos sem nome desta foto serão ignorados.", page.Markup, StringComparison.Ordinal);
        Assert.Equal("Apagar de vez", page.Find("form.confirm button").TextContent);
        Assert.Equal("E as cópias na nuvem?", page.Find("details.ignore-cloud summary").TextContent);
    }

    [Fact]
    public void Reaches_each_of_two_unnamed_faces_of_one_person_in_a_photo()
    {
        var crop = new FaceCrop(Ids.Of(1), [0.1, 0.1, 0.2, 0.2], 256, 192);
        var asset = PhotoWithFaces(new AssetFace(20, 2, null, crop), new AssetFace(21, 2, null, crop));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}/ignore-faces?face=21");

        var one = Render<PhotoIgnoreFaces>(p => p.Add(d => d.Id, asset.Id));

        Assert.Contains("1 unnamed face in this photo will be ignored.", one.Markup, StringComparison.Ordinal);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}/ignore-faces");
        var both = Render<PhotoIgnoreFaces>(p => p.Add(d => d.Id, asset.Id));
        Assert.Contains("2 unnamed faces in this photo will be ignored.", both.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_there_is_nothing_to_ignore_for_a_face_that_is_named_or_not_there()
    {
        var asset = PhotoWithFaces(new AssetFace(10, 1, "Ana", null));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}/ignore-faces?face=10");

        var page = Render<PhotoIgnoreFaces>(p => p.Add(d => d.Id, asset.Id));

        Assert.Equal("Nothing to ignore", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll("form.confirm"));
    }

    [Fact]
    public void Offers_to_ignore_an_unnamed_person_and_not_a_named_one()
    {
        _library.Persons[7] = PersonPages.Person(7, name: null, user: null);
        _library.Persons[8] = PersonPages.Person(8, name: "Ana", user: null);

        var unnamed = Render<Person>(p => p.Add(x => x.Id, 7L));
        var named = Render<Person>(p => p.Add(x => x.Id, 8L));

        Assert.Equal("people/7/ignore", unnamed.Find("div.ignore-person a").GetAttribute("href"));
        Assert.Equal("Ignore this person", unnamed.Find("div.ignore-person a").TextContent);
        // Each of the two deletions says what it is for.
        Assert.Contains("strangers", unnamed.Find("div.ignore-person .muted").TextContent, StringComparison.Ordinal);
        Assert.StartsWith("Deletes this person, their name and their faces.", unnamed.Find("div.delete-face-data .muted").TextContent, StringComparison.Ordinal);
        Assert.Equal("Danger zone", unnamed.Find(".danger-zone h3").TextContent);
        Assert.Equal(["Face data", "Stranger"], unnamed.FindAll(".danger-zone .form-label").Select(l => l.TextContent));
        Assert.Equal("false", unnamed.Find("div.ignore-person a").GetAttribute("data-enhance-nav"));
        Assert.Empty(named.FindAll("div.ignore-person"));
    }

    [Fact]
    public void Asks_before_ignoring_a_person_and_refuses_a_named_one()
    {
        _library.Persons[7] = PersonPages.Person(7, name: null, user: null);
        _library.Persons[8] = PersonPages.Person(8, name: "Ana", user: null);

        var unnamed = Render<PersonIgnore>(p => p.Add(x => x.Id, 7L));
        var named = Render<PersonIgnore>(p => p.Add(x => x.Id, 8L));

        Assert.Equal("Delete for good", unnamed.Find("form.confirm button").TextContent);
        Assert.True(unnamed.Find("form.confirm > a").HasAttribute("autofocus"));
        Assert.Empty(named.FindAll("form.confirm"));
        Assert.Contains("Only an unnamed person can be ignored", named.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "Ignore the unnamed person with one or two faces (1)")]
    [InlineData(5, "Ignore the unnamed people with one or two faces (5)")]
    public void Offers_the_bulk_action_on_people_only_when_some_unnamed_person_has_few_faces(int count, string? text)
    {
        _faces.FewFaces = count;
        _library.People.Add(new PersonSummary(8, "Bruno", 3, null));

        var page = Render<People>();

        if (text is null)
        {
            Assert.Empty(page.FindAll("p.ignore-many"));
        }
        else
        {
            Assert.Equal("people/ignore", page.Find("p.ignore-many a").GetAttribute("href"));
            Assert.Equal("false", page.Find("p.ignore-many a").GetAttribute("data-enhance-nav"));
            Assert.Equal(text, page.Find("p.ignore-many a").TextContent);
        }
    }

    [Fact]
    public void Asks_before_the_bulk_action_with_the_number_it_will_check_and_says_so_when_the_people_changed()
    {
        _faces.FewFaces = 30;
        _faces.FewFaceCrops.AddRange(Enumerable.Range(1, 30).Select(n => new FaceCrop(Ids.Of(n), [0.1, 0.1, 0.2, 0.2], 256, 192)));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/people/ignore?note=IgnoreChanged");

        var page = Render<PeopleIgnore>();

        Assert.Contains("30 unnamed people with one or two faces will be ignored.", page.Markup, StringComparison.Ordinal);
        // The warning once, after the crops.
        Assert.Single(page.FindAll("p.ignore-warning"));
        Assert.True(page.Markup.IndexOf("ignore-crops", StringComparison.Ordinal) < page.Markup.IndexOf("ignore-warning", StringComparison.Ordinal));
        // A strip of at most 24 faces, then how many more.
        Assert.Equal(24, page.FindAll("ul.ignore-crops li").Count);
        Assert.Equal("and 6 more people", page.Find("p.ignore-more").TextContent);
        Assert.True(page.Find("form.confirm > a").HasAttribute("autofocus"));
        Assert.Contains("autofocus", page.Find("form.confirm > a").OuterHtml, StringComparison.Ordinal);
        Assert.Equal("30", page.Find("form.confirm input[name=Expected]").GetAttribute("value"));
        Assert.Contains("changed since you opened this page", page.Find("p[role=status]").TextContent, StringComparison.Ordinal);
    }
}

public sealed class IgnoreFacesPostTests
{
    private const string Ana = "ana@example.com";

    [Fact]
    public async Task Ignores_one_face_or_all_the_unnamed_faces_of_a_photo_only_from_the_confirmation()
    {
        var (web, faces, asset) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var one = await FaceSettingsPostTests.PostAsync(http, $"/photos/{asset}/ignore-faces?face=20", "ignore", new() { ["Confirmed"] = "yes" });
        using var all = await FaceSettingsPostTests.PostAsync(http, $"/photos/{asset}/ignore-faces", "ignore", new() { ["Confirmed"] = "yes" });

        Assert.Equal($"/photos/{asset}?note=ignored", one.Headers.Location?.PathAndQuery);
        Assert.Equal($"/photos/{asset}?note=ignored", all.Headers.Location?.PathAndQuery);
        Assert.Equal([$"ignore-faces {asset[..4]} 20 {Ana}", $"ignore-faces {asset[..4]} all {Ana}"], faces.Calls);
    }

    [Fact]
    public async Task Goes_back_to_the_photo_without_a_note_when_nothing_was_ignored()
    {
        var (web, faces, asset) = Web();
        await using var _ = web;
        faces.CanIgnore = false;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await FaceSettingsPostTests.PostAsync(http, $"/photos/{asset}/ignore-faces", "ignore", new() { ["Confirmed"] = "yes" });

        Assert.Equal($"/photos/{asset}", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Ignores_an_unnamed_person_and_the_few_face_people_from_their_confirmations()
    {
        var (web, faces, _) = Web();
        await using var _ = web;
        faces.FewFaces = 4;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var person = await FaceSettingsPostTests.PostAsync(http, "/people/9/ignore", "ignore", new() { ["Confirmed"] = "yes" });
        using var many = await FaceSettingsPostTests.PostAsync(http, "/people/ignore", "ignore-many", new() { ["Expected"] = "4" });

        Assert.Equal("/people?note=ignored", person.Headers.Location?.PathAndQuery);
        Assert.Equal("/people?note=ignored", many.Headers.Location?.PathAndQuery);
        Assert.Equal([$"ignore-person 9 {Ana}", $"ignore-many 4 {Ana}"], faces.Calls);
    }

    [Fact]
    public async Task Says_the_people_changed_when_the_catalog_refuses_the_number()
    {
        var (web, faces, _) = Web();
        await using var _ = web;
        faces.FewFaces = 4;
        faces.CanIgnore = false;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await FaceSettingsPostTests.PostAsync(http, "/people/ignore", "ignore-many", new() { ["Expected"] = "4" });

        Assert.Equal("/people/ignore?note=IgnoreChanged", response.Headers.Location?.PathAndQuery);
    }

    [Theory]
    [InlineData("/people/ignore")]
    [InlineData("/people/9/ignore")]
    public async Task Needs_a_sign_in(string address)
    {
        var (web, _, _) = Web();
        await using var __ = web;
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(new Uri(address, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static (WebFactory Web, FakeFaceSettings Faces, string Asset) Web()
    {
        var library = new FakeLibrary();
        var asset = Details.Photo(Ids.Of(1), new DateTime(2019, 7, 14)) with { Faces = [new AssetFace(20, 2, null, null), new AssetFace(30, 3, null, null)], People = [new AssetPerson(2, null, 20), new AssetPerson(3, null, 30)] };
        library.Assets[asset.Id] = asset;
        library.Persons[9] = PersonPages.Person(9, name: null, user: null);
        var faces = new FakeFaceSettings { FewFaces = 2 };
        return (new WebFactory(library) { AccessEmails = Ana, FaceSettings = faces }, faces, asset.Id);
    }
}

/// <summary>The names of the fields the confirmation pages post: the form binding reads them by name, so a rename must fail a test.</summary>
public sealed class ConfirmationFieldNameTests : LocalizedContext
{
    private readonly FakeLibrary _library = new();

    public ConfirmationFieldNameTests()
    {
        Services.AddSingleton<IFaceSettings>(new FakeFaceSettings());
        Services.AddSingleton<ILibraryQueries>(_library);
        Services.AddSingleton<IPeopleEditor>(new FakePeopleEditor());
        _library.Persons[7] = PersonPages.Person(7, name: "Ana", user: null);
        _library.Persons[9] = PersonPages.Person(9, name: null, user: null);
    }

    [Fact]
    public void The_merge_page_posts_confirmed_and_swap()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("people/7/merge?other=9");

        var form = Render<PersonMerge>(p => p.Add(x => x.Id, 7L)).Find("form.confirm");

        Assert.Equal("yes", form.QuerySelector("input[name=Confirmed]")!.GetAttribute("value"));
        Assert.Equal("0", form.QuerySelector("input[name=Swap]")!.GetAttribute("value"));
    }

    [Fact]
    public void The_ignore_delete_and_photo_ignore_pages_post_confirmed()
    {
        var asset = Details.Photo(Ids.Of(1), new DateTime(2019, 7, 14, 10, 32, 0)) with { Faces = [new AssetFace(20, 9, null, null)] };
        _library.Assets[asset.Id] = asset;

        Services.GetRequiredService<NavigationManager>().NavigateTo("people/9/ignore");
        var person = Render<PersonIgnore>(p => p.Add(x => x.Id, 9L)).Find("form.confirm input[name=Confirmed]");
        Services.GetRequiredService<NavigationManager>().NavigateTo("people/7/delete");
        var delete = Render<PersonDelete>(p => p.Add(x => x.Id, 7L)).Find("form.confirm input[name=Confirmed]");
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}/ignore-faces");
        var photo = Render<PhotoIgnoreFaces>(p => p.Add(x => x.Id, asset.Id)).Find("form.confirm input[name=Confirmed]");

        Assert.All(new[] { person, delete, photo }, input => Assert.Equal("yes", input.GetAttribute("value")));
    }

    [Fact]
    public void The_move_page_posts_target_and_the_person_page_posts_me()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("people/7/faces/70/move");
        var move = Render<FaceMove>(p => p.Add(x => x.Id, 7L).Add(x => x.FaceId, 70L));
        Assert.NotNull(move.Find("form.move select[name=Target]"));

        Services.GetRequiredService<NavigationManager>().NavigateTo("people/7");
        var person = Render<Person>(p => p.Add(x => x.Id, 7L));
        Assert.Equal(["link"], person.FindAll("form.me button[name=Me]").Select(b => b.GetAttribute("value")));
    }
}
