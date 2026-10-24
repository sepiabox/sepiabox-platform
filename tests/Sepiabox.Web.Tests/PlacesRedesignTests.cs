using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The places page on the shared components (S-071, D-119): the form card, the home option, cards with a folded edit form and an empty state.</summary>
public sealed class PlacesRedesignTests : LocalizedContext
{
    private readonly FakeSavedPlaces _places = new();

    public PlacesRedesignTests()
    {
        Services.AddSingleton<ISavedPlaces>(_places);
        Services.AddSingleton<ILibraryQueries>(new FakeLibrary());
    }

    [Fact]
    public void Offers_the_home_as_an_option_card_with_what_it_is_for()
    {
        var page = Render<Places>();

        var home = page.Find(".new-place label.option-card");
        Assert.Equal("This is home", home.QuerySelector("strong")!.TextContent);
        Assert.Contains("from home", home.QuerySelector(".field-help")!.TextContent, StringComparison.Ordinal);
        Assert.False(home.QuerySelector("input")!.HasAttribute("checked"));
        Assert.Single(page.FindAll(".new-place .form-actions button.button"));
    }

    [Fact]
    public void Shows_an_empty_state_whose_first_action_adds_the_home()
    {
        var page = Render<Places>();

        Assert.Equal("No places yet", page.Find(".empty-state .empty-title").TextContent);
        var action = page.Find(".empty-state .empty-action a.button");
        Assert.Equal("Add your home", action.TextContent);
        Assert.Equal("places?home=1#new-place-heading", action.GetAttribute("href"));
        Assert.Equal("new-place-heading", page.Find(".new-place h2").Id);
    }

    [Fact]
    public void Opens_the_form_as_the_home_named_home_after_the_empty_state_s_action()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("places?home=1");

        var page = Render<Places>();

        Assert.Equal("Home", page.Find("input[name=NewName]").GetAttribute("value"));
        Assert.True(page.Find("input[name=NewHome]").HasAttribute("checked"));
    }

    [Fact]
    public void Says_casa_in_brazilian_portuguese_for_the_home()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        Services.GetRequiredService<NavigationManager>().NavigateTo("places?home=1");

        var page = Render<Places>();

        Assert.Equal("Casa", page.Find("input[name=NewName]").GetAttribute("value"));
        Assert.Equal("Adicionar sua casa", page.Find(".empty-action a.button").TextContent);
    }

    [Fact]
    public void Draws_a_card_per_place_with_the_edit_form_folded_and_delete_as_a_danger_action()
    {
        _places.Places.Add(new SavedPlaceSummary(1, "Casa", 38.74, -9.16, 300, IsHome: true));
        _places.Places.Add(new SavedPlaceSummary(2, "Praia", 38.96, -9.41, 500, IsHome: false));

        var page = Render<Places>();

        Assert.Equal("2", page.Find(".section-title .n").TextContent);
        Assert.Empty(page.FindAll(".empty-state"));
        var cards = page.FindAll("li.saved-place");
        Assert.Equal(2, cards.Count);
        Assert.Contains("is-home", cards[0].ClassList);
        Assert.All(cards, card =>
        {
            var edit = card.QuerySelector("details.place-edit")!;
            Assert.False(edit.HasAttribute("open"));
            Assert.Equal("Edit", edit.QuerySelector("summary")!.TextContent);
            Assert.NotNull(edit.QuerySelector("form.edit .option-card input[name=EditHome]"));
        });
        var delete = cards[1].QuerySelector("a.button.danger.delete")!;
        Assert.Equal("places?delete=2", delete.GetAttribute("href"));
        Assert.Equal(2, cards[1].QuerySelectorAll("a.button.secondary").Length);
    }

    [Fact]
    public void Asks_before_deleting_with_a_warning_and_a_danger_button()
    {
        _places.Places.Add(new SavedPlaceSummary(2, "Praia", 38.96, -9.41, 500, IsHome: false));
        Services.GetRequiredService<NavigationManager>().NavigateTo("places?delete=2");

        var page = Render<Places>();

        Assert.Contains("Praia", page.Find(".confirm-delete .note-warning").TextContent, StringComparison.Ordinal);
        Assert.Equal("Delete Praia", page.Find(".confirm-delete .form-actions button.button.danger").TextContent);
    }

    [Fact]
    public void Puts_the_empty_state_before_the_form_and_focuses_the_coordinates_for_the_home()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("places?home=1");

        var page = Render<Places>();

        var markup = page.Markup;
        Assert.True(markup.IndexOf("empty-state", StringComparison.Ordinal) < markup.IndexOf("new-place", StringComparison.Ordinal));
        Assert.True(page.Find("input[name=NewCoordinates]").HasAttribute("autofocus"));
        Assert.False(page.Find("input[name=NewName]").HasAttribute("autofocus"));
    }

    [Fact]
    public void Does_not_fill_the_form_in_again_once_it_was_sent_with_the_home_still_in_the_address()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("places?home=1");
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.ContentType = "application/x-www-form-urlencoded";

        var page = Render<Places>(p => p.AddCascadingValue<Microsoft.AspNetCore.Http.HttpContext>(http));

        Assert.Null(page.Find("input[name=NewName]").GetAttribute("value"));
        Assert.False(page.Find("input[name=NewHome]").HasAttribute("checked"));
        Assert.False(page.Find("input[name=NewCoordinates]").HasAttribute("autofocus"));
    }

    [Fact]
    public async Task Shows_a_refused_edit_inside_its_own_card_as_an_alert_and_not_at_the_top()
    {
        _places.Places.Add(new SavedPlaceSummary(1, "Casa", 38.74, -9.16, 300, IsHome: true));
        _places.Places.Add(new SavedPlaceSummary(2, "Praia", 38.96, -9.41, 500, IsHome: false));
        _places.Outcome = Sepiabox.Catalog.SavedPlaces.SavedPlaceOutcome.NameTaken;
        var page = Render<Places>();
        page.Instance.EditName = "Casa";

        await page.InvokeAsync(() => page.Find("#place-2 form.edit").Submit());

        Assert.Single(page.FindAll(".note"));
        // Only the refused place opens its edit form; the other stays folded.
        Assert.Equal(["place-2"], page.FindAll("details.place-edit[open]").Select(d => d.ParentElement!.Id));
        var refusal = page.Find("#place-2 details.place-edit[open] .edit-refusal");
        Assert.Equal("alert", refusal.GetAttribute("role"));
        Assert.Equal("places#place-2", page.Find("#place-2 form.edit").GetAttribute("action"));
        Assert.Empty(page.FindAll("#place-1 .note"));
    }

    [Fact]
    public void Names_the_places_with_h3_and_shows_the_home_once_as_a_chip_with_the_long_hint_only_there()
    {
        _places.Places.Add(new SavedPlaceSummary(1, "Casa", 38.74, -9.16, 300, IsHome: true));
        _places.Places.Add(new SavedPlaceSummary(2, "Praia", 38.96, -9.41, 500, IsHome: false));

        var page = Render<Places>();

        Assert.Equal(["Casa", "Praia"], page.FindAll("li.saved-place > h3").Select(h => h.FirstChild!.TextContent.Trim()));
        Assert.Equal("Home", Assert.Single(page.FindAll(".chip")).TextContent);
        var hints = page.FindAll("li.saved-place .option-card .field-help").Select(h => h.TextContent).ToList();
        Assert.Contains("Unlocks", hints[0], StringComparison.Ordinal);
        Assert.Equal("Make this place your home.", hints[1]);
    }

    [Fact]
    public void Names_the_fields_the_server_binds_in_the_create_form_and_in_each_edit_form()
    {
        _places.Places.Add(new SavedPlaceSummary(1, "Casa", 38.74, -9.16, 300, IsHome: true));
        _places.Places.Add(new SavedPlaceSummary(2, "Praia", 38.96, -9.41, 500, IsHome: false));

        var page = Render<Places>();

        static string[] Names(AngleSharp.Dom.IElement form) =>
            form.QuerySelectorAll("input[name]").Select(i => i.GetAttribute("name")!).Where(n => n != "__RequestVerificationToken").ToArray();
        Assert.Equal(["NewName", "NewCoordinates", "NewRadius", "NewHome"], Names(page.Find("form.create")));
        Assert.All(page.FindAll("form.edit"), form => Assert.Equal(["EditName", "EditRadius", "EditHome"], Names(form)));
        Assert.Equal(["number", "checkbox"], page.Find("#place-2 form.edit").QuerySelectorAll("input[name=EditRadius], input[name=EditHome]").Select(i => i.GetAttribute("type")!));
        Assert.Equal("500", page.Find("#place-2 input[name=EditRadius]").GetAttribute("value"));
    }
}
