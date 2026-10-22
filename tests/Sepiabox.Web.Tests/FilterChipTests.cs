using Sepiabox.Web.Components.People;
using Sepiabox.Web.Components.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The dropdown chips of the filter bar and the avatar of the people picker (S-067, D-123).</summary>
public sealed class FilterChipTests : LocalizedContext
{
    private static readonly FilterOptions Catalog = new([2021, 2019], ["Lisbon"], []) { People = ["Ana Costa", "Pedro"], MeLinked = true };

    [Fact]
    public void Shows_each_chip_as_any_until_a_value_is_set_and_as_label_and_value_after()
    {
        var empty = Render<FilterBar>(p => p.Add(b => b.Options, Catalog));
        var year = empty.Find("details.fchip");

        Assert.False(year.HasAttribute("data-set"));
        Assert.Equal("Year", year.QuerySelector("summary .k")!.TextContent);
        Assert.Equal("Any", year.QuerySelector("summary .v")!.TextContent);

        var fields = new FilterFields(2019, null, null, null, null);
        var set = Render<FilterBar>(p => p.Add(b => b.Options, Catalog).Add(b => b.Fields, fields)).Find("details.fchip");

        Assert.True(set.HasAttribute("data-set"));
        Assert.Equal("Year:", set.QuerySelector("summary .k")!.TextContent);
        Assert.Equal("2019", set.QuerySelector("summary .v")!.TextContent);
    }

    [Fact]
    public void Puts_any_first_in_every_radio_group_and_checks_it_when_nothing_is_set()
    {
        var bar = Render<FilterBar>(p => p.Add(b => b.Options, Catalog));

        var years = bar.FindAll("input[type=radio][name=year]");

        Assert.Equal(["", "2021", "2019"], years.Select(r => r.GetAttribute("value")));
        Assert.Equal([""], bar.FindAll("input[name=year][checked]").Select(r => r.GetAttribute("value")));
    }

    [Fact]
    public void Offers_the_people_as_searchable_checkboxes_with_reset_and_show_results()
    {
        var bar = Render<FilterBar>(p => p.Add(b => b.Options, Catalog).Add(b => b.PeopleResetHref, "./?filter=1"));

        Assert.All(bar.FindAll("input[name=person]"), i => Assert.Equal("checkbox", i.GetAttribute("type")));
        Assert.NotNull(bar.Find("input[data-filter-search]"));
        Assert.Equal("./?filter=1", bar.Find(".pop-foot a").GetAttribute("href"));
        Assert.Equal("submit", bar.Find(".pop-foot button").GetAttribute("type"));
        Assert.Equal(["AC", "P"], bar.FindAll("label.opt .avatar").Select(a => a.TextContent.Trim()));
        Assert.Equal("Filter", bar.Find("button.button[type=submit]:not(.pop-foot button)").TextContent.Trim());
    }

    [Fact]
    public void Offers_with_me_and_on_this_day_as_pills()
    {
        var bar = Render<FilterBar>(p => p.Add(b => b.Options, Catalog));

        Assert.NotNull(bar.Find("label.pill input[name=me]"));
        Assert.NotNull(bar.Find("a.pill.shortcut"));
    }

    [Theory]
    [InlineData("Ana Costa", "AC")]
    [InlineData("ana maria de souza", "AS")]
    [InlineData("Pedro", "P")]
    [InlineData("  ", "?")]
    [InlineData(null, "?")]
    public void Takes_the_initials_from_the_first_and_last_words(string? name, string expected)
    {
        Assert.Equal(expected, PersonAvatar.InitialsOf(name));
    }

    [Fact]
    public void Colours_an_avatar_by_person_id_and_marks_an_unnamed_one()
    {
        var named = Render<PersonAvatar>(p => p.Add(a => a.Name, "Ana").Add(a => a.PersonId, 5L).Add(a => a.Size, "lg")).Find(".avatar");
        var unnamed = Render<PersonAvatar>(p => p.Add(a => a.PersonId, 4L)).Find(".avatar");

        Assert.Equal("avatar lg c3", named.ClassName);
        Assert.Equal("avatar c2 unnamed", unnamed.ClassName);
    }
}
