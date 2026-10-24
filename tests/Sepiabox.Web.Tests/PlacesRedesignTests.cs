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
        Assert.Equal("Delete", page.Find(".confirm-delete button.button.danger").TextContent);
    }
}
