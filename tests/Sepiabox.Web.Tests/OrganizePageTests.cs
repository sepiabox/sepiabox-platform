using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The organize page's markup (S-027, D-098): what a person with a screen reader or a phone meets, in both languages.</summary>
public sealed class OrganizePageTests : LocalizedContext
{
    private readonly FakeOrganization _organization = new();

    public OrganizePageTests()
    {
        Services.AddSingleton<IOrganization>(_organization);
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgery());
    }

    [Theory]
    [InlineData(Cultures.English, "Set aside", "Sharpest")]
    [InlineData(Cultures.BrazilianPortuguese, "Ocultar", "Mais nítida")]
    public void A_burst_labels_every_box_the_same_and_marks_the_sharpest_with_a_badge_and_an_unchecked_box(string culture, string label, string badge)
    {
        using var _ = Cultures.Use(culture);
        _organization.Bursts = [new BurstGroup([Item(1, 300), Item(2, 800), Item(3, 120)], Ids.Of(2))];

        var page = Open("/organize");

        var boxes = page.FindAll("input[name=Ids]");
        Assert.Equal([true, false, true], boxes.Select(b => b.HasAttribute("checked")));
        Assert.All(page.FindAll("label.check"), l => Assert.StartsWith(label, l.TextContent.Trim(), StringComparison.Ordinal));
        Assert.Equal(badge, Assert.Single(page.FindAll(".sharpest-badge")).TextContent);
        Assert.Equal(Ids.Of(2), page.FindAll(".organize-item").Single(i => i.QuerySelector(".sharpest-badge") is not null).QuerySelector("input")!.GetAttribute("value"));
    }

    [Fact]
    public void Shows_the_lists_as_a_segmented_control_with_a_count_on_each_tab_the_bursts_only_when_shown()
    {
        _organization.Bursts = [new BurstGroup([Item(1, 300), Item(2, 800)], Ids.Of(2)), new BurstGroup([Item(3, 300), Item(4, 800)], Ids.Of(4))];
        _organization.Blurry = [Item(5, 10), Item(6, 20), Item(7, 30)];
        _organization.Hidden = [Item(8, null)];

        var tabs = Open("/organize").FindAll("nav.segmented .seg");

        Assert.Equal(["Bursts2", "Blurry3", "Set aside1"], tabs.Select(t => t.TextContent));
        Assert.Equal("page", tabs[0].GetAttribute("aria-current"));
        Assert.All(tabs.Skip(1), t => Assert.Equal("a", t.LocalName));
        // The bursts are not counted while another list is shown (finding them reads every photo).
        Assert.Equal(["Bursts", "Blurry3", "Set aside1"], Open("/organize?view=blurry").FindAll("nav.segmented .seg").Select(t => t.TextContent));
    }

    [Theory]
    [InlineData(Cultures.English, "Set aside 1 checked photo", "Set aside the checked photos", "Set aside {0} checked photos")]
    [InlineData(Cultures.BrazilianPortuguese, "Ocultar 1 foto marcada", "Ocultar as fotos marcadas", "Ocultar {0} fotos marcadas")]
    public void Puts_the_action_in_a_bar_at_the_bottom_that_says_how_many_are_checked_and_carries_the_other_wordings_for_the_script(string culture, string one, string zero, string other)
    {
        using var _ = Cultures.Use(culture);
        // A burst's sharpest photo is unchecked, so the rest of two bursts of two are checked: two photos.
        _organization.Bursts = [new BurstGroup([Item(1, 300), Item(2, 800)], Ids.Of(2)), new BurstGroup([Item(3, 300), Item(4, 800)], Ids.Of(4))];
        _organization.Blurry = [Item(5, 10)];

        var bursts = Open("/organize").Find("form.organize-form .organize-bar button");

        Assert.Equal(other.Replace("{0}", "2", StringComparison.Ordinal), bursts.TextContent);
        Assert.Equal((zero, one, other), (bursts.GetAttribute("data-zero"), bursts.GetAttribute("data-one"), bursts.GetAttribute("data-other")));
        // Nothing is checked on the blurry list on arrival.
        var blurry = Open("/organize?view=blurry").Find("form.organize-form .organize-bar button");
        Assert.Equal(zero, blurry.TextContent);
        Assert.Equal("hide", blurry.GetAttribute("value"));
    }

    [Fact]
    public void Brings_back_has_its_own_bar_with_the_matching_words()
    {
        _organization.Hidden = [Item(8, null)];

        var button = Open("/organize?view=hidden").Find(".organize-bar button");

        Assert.Equal("Bring back the checked photos", button.TextContent);
        Assert.Equal(("unhide", "Bring back 1 checked photo", "Bring back {0} checked photos"), (button.GetAttribute("value"), button.GetAttribute("data-one"), button.GetAttribute("data-other")));
    }

    [Fact]
    public void Each_box_is_named_for_its_photo_and_each_photo_opens_in_a_new_tab_with_the_larger_preview()
    {
        _organization.Bursts = [new BurstGroup([Item(1, 300), Item(2, 800)], Ids.Of(2))];

        var page = Open("/organize");

        var names = page.FindAll("label.check").Select(l => l.TextContent).ToList();
        Assert.Equal(2, names.Distinct().Count());
        Assert.Contains("Photo, July 14, 2019", names[0], StringComparison.Ordinal);
        Assert.Contains("10:31", names[0], StringComparison.Ordinal);
        Assert.Contains("10:32", names[1], StringComparison.Ordinal);
        var link = page.FindAll("a.thumb")[0];
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"), StringComparison.Ordinal);
        Assert.EndsWith("opens in a new tab", link.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Equal($"photos/{Ids.Of(1)}/preview-1280", link.QuerySelector("img")!.GetAttribute("src"));
    }

    [Theory]
    [InlineData(Cultures.English, "Very blurry", "Blurry", "Sharp")]
    [InlineData(Cultures.BrazilianPortuguese, "Muito desfocada", "Desfocada", "Nítida")]
    public void Says_how_sharp_a_photo_is_in_a_word_and_keeps_the_number_in_a_title(string culture, string veryBlurry, string blurry, string sharp)
    {
        using var _ = Cultures.Use(culture);
        _organization.Blurry = [Item(1, 10), Item(2, 70), Item(3, 150)];

        var page = Open("/organize?view=blurry");

        var levels = page.FindAll(".level");
        Assert.Equal([veryBlurry, blurry, sharp], levels.Select(l => l.TextContent));
        Assert.Contains("10", levels[0].GetAttribute("title"), StringComparison.Ordinal);
        Assert.DoesNotContain("100", page.Find("p.muted.small").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_the_2048_preview_of_an_index_only_photo_and_the_thumbnail_when_there_is_no_preview()
    {
        _organization.Blurry = [Item(1, 10, DerivativeNames.LargePreview), Item(2, 10, null)];

        var sources = Open("/organize?view=blurry").FindAll("a.thumb img").Select(i => i.GetAttribute("src")).ToList();

        Assert.Equal([$"photos/{Ids.Of(1)}/preview-2048", $"photos/{Ids.Of(2)}/thumb-256"], sources);
    }

    [Fact]
    public void Picks_the_largest_preview_a_photo_has_in_the_detail_page_s_order()
    {
        Assert.Equal("preview-2048", OrganizeItem.LargestPreview(["thumb-256", "preview-1280", "preview-2048"]));
        Assert.Equal("preview-1280", OrganizeItem.LargestPreview(["thumb-256", "preview-1280"]));
        Assert.Null(OrganizeItem.LargestPreview(["thumb-256"]));
    }

    [Fact]
    public void Buttons_are_styled_buttons_and_the_bulk_helpers_are_hidden_until_the_script_runs()
    {
        _organization.Blurry = [Item(1, 10)];

        var page = Open("/organize?view=blurry");

        Assert.All(page.FindAll("form button[type=submit]"), b => Assert.Contains("button", b.ClassList));
        Assert.True(page.Find("[data-bulk]").HasAttribute("hidden"));
        Assert.Equal(["Select all on this page", "Select none"], page.FindAll("[data-bulk] button").Select(b => b.TextContent));
        Assert.Contains("Checks apply to this page only.", page.Find("p.muted.small").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hidden_list_has_the_bulk_helpers_and_a_bring_back_label()
    {
        _organization.Hidden = [Item(1, null)];

        var page = Open("/organize?view=hidden");

        Assert.NotNull(page.Find("[data-bulk]"));
        Assert.StartsWith("Bring back", page.Find("label.check").TextContent.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void Starts_with_the_shared_head_the_views_as_pills_and_the_intro_under_the_title()
    {
        var page = Open("/organize");

        Assert.Equal("Organize", page.Find("div.page-head h1.page-heading").TextContent);
        Assert.NotEmpty(page.FindAll("div.page-head .page-sub"));
        Assert.NotEmpty(page.FindAll("nav.organize-views a, nav.organize-views strong"));
    }

    private IRenderedComponent<Organize> Open(string address)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        return Render<Organize>();
    }

    private static OrganizeItem Item(int n, double? sharpness, string? previewName = DerivativeNames.Preview) =>
        new(new TimelineItem(Ids.Of(n), AssetKinds.Photo, new DateTime(2019, 7, 14, 10, 30 + n, 0), true, null), sharpness, previewName);

    private sealed class FixedAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }

    private sealed class FakeOrganization : IOrganization
    {
        public IReadOnlyList<BurstGroup> Bursts { get; set; } = [];

        public IReadOnlyList<OrganizeItem> Blurry { get; set; } = [];

        public IReadOnlyList<OrganizeItem> Hidden { get; set; } = [];

        public double BlurryBelow => 100;

        public Task<OrganizePage<BurstGroup>> GetBurstsAsync(int page, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrganizePage<BurstGroup>(page, Bursts, false, Bursts.Count));

        public Task<OrganizePage<OrganizeItem>> GetBlurryAsync(int page, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrganizePage<OrganizeItem>(page, Blurry, false, Blurry.Count));

        public Task<OrganizePage<OrganizeItem>> GetHiddenAsync(int page, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrganizePage<OrganizeItem>(page, Hidden, false, Hidden.Count));

        public Task<OrganizeCounts> GetCountsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new OrganizeCounts(Blurry.Count, Hidden.Count));

        public Task<int> HideAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> UnhideAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
