using Sepiabox.Web.Components.People;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The people page (S-019): order, names and the face crop. Only synthetic data; no real face.</summary>
public sealed class PeopleGridTests : LocalizedContext
{
    [Fact]
    public void Lists_people_in_descending_order_of_faces()
    {
        var grid = Render<PeopleGrid>(p => p.Add(g => g.People, [
            new PersonSummary(1, "Bruno", 2, null),
            new PersonSummary(2, null, 7, null),
            new PersonSummary(3, "Ana", 5, null),
            new PersonSummary(4, "Carla", 5, null),
        ]));

        Assert.Equal(["Unnamed", "Ana", "Carla", "Bruno"], grid.FindAll(".person .name").Select(n => n.TextContent));
        Assert.Equal(["7 faces", "5 faces", "5 faces", "2 faces"], grid.FindAll(".person .count").Select(n => n.TextContent));
    }

    [Fact]
    public void Says_sem_nome_and_rosto_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var grid = Render<PeopleGrid>(p => p.Add(g => g.People, [new PersonSummary(1, null, 1, null)]));

        Assert.Equal("Sem nome", grid.Find(".name").TextContent);
        Assert.Equal("1 rosto", grid.Find(".count").TextContent);
    }

    [Fact]
    public void Crops_the_face_from_the_thumbnail_with_the_relative_box()
    {
        var cover = new FaceCrop(Ids.Of(1), [0.25, 0.25, 0.5, 0.5], 256, 192);

        var grid = Render<PeopleGrid>(p => p.Add(g => g.People, [new PersonSummary(1, "Ana", 1, cover)]));

        var img = grid.Find(".face img");
        Assert.Equal($"photos/{Ids.Of(1)}/thumb-256", img.GetAttribute("src"));
        Assert.Equal(cover.ImageStyle(), img.GetAttribute("style"));
        Assert.Equal(string.Empty, img.GetAttribute("alt"));
    }

    [Fact]
    public void Shows_an_empty_face_when_there_is_no_thumbnail()
    {
        var grid = Render<PeopleGrid>(p => p.Add(g => g.People, [new PersonSummary(1, "Ana", 1, null)]));

        Assert.Empty(grid.FindAll("img"));
        Assert.Single(grid.FindAll(".face"));
    }
}

public sealed class FaceCropTests
{
    [Fact]
    public void Fills_a_square_frame_with_the_face_and_its_margin()
    {
        // A 64x64 face at (96, 64) on a 256x192 thumbnail: an 83.2 px square centred on (128, 96).
        var crop = new FaceCrop(Ids.Of(1), [0.375, 1 / 3.0, 0.25, 1 / 3.0], 256, 192);

        Assert.Equal("width:307.692%;height:230.769%;left:-103.846%;top:-65.385%", crop.ImageStyle());
    }

    [Fact]
    public void Keeps_the_square_inside_the_thumbnail_when_the_face_touches_an_edge()
    {
        var crop = new FaceCrop(Ids.Of(1), [0.0, 0.0, 0.2, 0.2], 200, 100);

        // A 52 px square centred on (20, 10), moved back inside at the top left corner.
        Assert.Equal("width:384.615%;height:192.308%;left:0%;top:0%", crop.ImageStyle());
    }

    [Fact]
    public void Never_crops_more_than_the_thumbnail_s_shorter_side()
    {
        var crop = new FaceCrop(Ids.Of(1), [0.0, 0.0, 1.0, 1.0], 256, 192);

        // The whole height, centred across: 192 px wide from x = 32.
        Assert.Equal("width:133.333%;height:100%;left:-16.667%;top:0%", crop.ImageStyle());
    }
}

/// <summary>The page over HTTP, signed in, on an in-memory library.</summary>
public sealed class PeoplePageTests
{
    [Fact]
    public async Task Shows_the_people_with_unnamed_ones_when_signed_in()
    {
        await using var web = new WebFactory(new FakeLibrary { People = [new PersonSummary(1, null, 3, null), new PersonSummary(2, "Ana", 1, null)] });
        using var http = web.CreateSignedInClient();

        var html = await http.GetStringAsync(new Uri("/people", UriKind.Relative));

        // Only the page's own markup: the header's sign-out and language forms carry a fresh antiforgery token (base64url,
        // about 150 characters) on every request, and a short name such as "Ana" turns up in one about once in a thousand.
        var main = html[html.IndexOf("<main", StringComparison.Ordinal)..];
        var (named, needsName, unnamed) = (main.IndexOf(">Ana<", StringComparison.Ordinal), main.IndexOf("Needs a name", StringComparison.Ordinal), main.IndexOf(">Unnamed<", StringComparison.Ordinal));
        Assert.True(named >= 0 && needsName > named && unnamed > needsName, $"Ana at {named}, the section at {needsName}, Unnamed at {unnamed}.");
        Assert.Contains("1 person still needs a name", main, StringComparison.Ordinal);
        Assert.Contains("3 faces", html, StringComparison.Ordinal);
        Assert.Contains("href=\"people\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_out_the_callout_and_the_sections_when_every_person_has_a_name()
    {
        await using var web = new WebFactory(new FakeLibrary { People = [new PersonSummary(2, "Ana", 1, null)] });
        using var http = web.CreateSignedInClient();

        var html = await http.GetStringAsync(new Uri("/people", UriKind.Relative));

        Assert.DoesNotContain("class=\"callout\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("section-title", html, StringComparison.Ordinal);
        Assert.Contains("class=\"person\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_there_are_no_people_yet_when_no_faces_were_grouped()
    {
        await using var web = new WebFactory(new FakeLibrary());
        using var http = web.CreateSignedInClient();

        var html = await http.GetStringAsync(new Uri("/people", UriKind.Relative));

        Assert.Contains("No people yet", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Needs_a_sign_in()
    {
        await using var web = new WebFactory(new FakeLibrary());
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(new Uri("/people", UriKind.Relative));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }
}
