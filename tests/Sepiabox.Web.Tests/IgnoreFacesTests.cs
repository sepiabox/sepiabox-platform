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

    [Fact]
    public void Offers_to_ignore_each_unnamed_face_of_a_photo_and_never_a_named_one()
    {
        var asset = PhotoWith(new AssetPerson(1, "Ana", 10), new AssetPerson(2, null, 20), new AssetPerson(3, null, 30));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}");

        var page = Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));

        Assert.Equal(
            [$"photos/{asset.Id}/ignore-faces?face=20", $"photos/{asset.Id}/ignore-faces?face=30"],
            page.FindAll("a.ignore-face").Select(a => a.GetAttribute("href")));
        Assert.Equal("Ignore this face", page.Find("a.ignore-face").TextContent);
        Assert.Equal($"photos/{asset.Id}/ignore-faces", page.Find("p.ignore-faces a").GetAttribute("href"));
        Assert.Equal("Ignore the unnamed faces in this photo", page.Find("p.ignore-faces a").TextContent);
    }

    [Fact]
    public void Offers_only_the_one_link_for_a_photo_with_one_unnamed_face_and_none_for_named_people()
    {
        var one = PhotoWith(new AssetPerson(1, "Ana", 10), new AssetPerson(2, null, 20));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{one.Id}");

        var page = Render<PhotoDetail>(p => p.Add(d => d.Id, one.Id));

        Assert.Single(page.FindAll("a.ignore-face"));
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
        var asset = PhotoWith(new AssetPerson(1, "Ana", 10), new AssetPerson(2, null, 20), new AssetPerson(3, null, 30));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}/ignore-faces?face=20");

        var page = Render<PhotoIgnoreFaces>(p => p.Add(d => d.Id, asset.Id));

        Assert.Equal("1 unnamed face in this photo will be ignored.", page.Find("p.note").TextContent);
        Assert.Contains("deleted for good", page.Markup, StringComparison.Ordinal);
        Assert.Contains("people with a name are never touched", page.Markup, StringComparison.Ordinal);
        Assert.Equal("Ignore for good", page.Find("form.confirm button").TextContent);
        Assert.Empty(_faces.Calls);
    }

    [Fact]
    public void Counts_every_unnamed_face_when_none_is_named_and_says_so_in_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        var asset = PhotoWith(new AssetPerson(2, null, 20), new AssetPerson(3, null, 30));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}/ignore-faces");

        var page = Render<PhotoIgnoreFaces>(p => p.Add(d => d.Id, asset.Id));

        Assert.Equal("2 rostos sem nome desta foto serão ignorados.", page.Find("p.note").TextContent);
        Assert.Equal("Ignorar de vez", page.Find("form.confirm button").TextContent);
    }

    [Fact]
    public void Says_there_is_nothing_to_ignore_for_a_face_that_is_named_or_not_there()
    {
        var asset = PhotoWith(new AssetPerson(1, "Ana", 10));
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

        Assert.Equal("people/7/ignore", unnamed.Find("p.ignore-person a").GetAttribute("href"));
        Assert.Equal("Ignore this person", unnamed.Find("p.ignore-person a").TextContent);
        Assert.Empty(named.FindAll("p.ignore-person"));
    }

    [Fact]
    public void Asks_before_ignoring_a_person_and_refuses_a_named_one()
    {
        _library.Persons[7] = PersonPages.Person(7, name: null, user: null);
        _library.Persons[8] = PersonPages.Person(8, name: "Ana", user: null);

        var unnamed = Render<PersonIgnore>(p => p.Add(x => x.Id, 7L));
        var named = Render<PersonIgnore>(p => p.Add(x => x.Id, 8L));

        Assert.Equal("Ignore for good", unnamed.Find("form.confirm button").TextContent);
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
            Assert.Equal(text, page.Find("p.ignore-many a").TextContent);
        }
    }

    [Fact]
    public void Asks_before_the_bulk_action_with_the_number_it_will_check_and_says_so_when_the_people_changed()
    {
        _faces.FewFaces = 5;
        Services.GetRequiredService<NavigationManager>().NavigateTo("/people/ignore?note=IgnoreChanged");

        var page = Render<PeopleIgnore>();

        Assert.Equal("5 unnamed people with one or two faces will be ignored.", page.FindAll("p.note")[1].TextContent);
        Assert.Equal("5", page.Find("form.confirm input[name=Expected]").GetAttribute("value"));
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
        var asset = Details.Photo(Ids.Of(1), new DateTime(2019, 7, 14)) with { People = [new AssetPerson(2, null, 20), new AssetPerson(3, null, 30)] };
        library.Assets[asset.Id] = asset;
        library.Persons[9] = PersonPages.Person(9, name: null, user: null);
        var faces = new FakeFaceSettings { FewFaces = 2 };
        return (new WebFactory(library) { AccessEmails = Ana, FaceSettings = faces }, faces, asset.Id);
    }
}
