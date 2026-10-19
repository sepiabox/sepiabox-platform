using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class TimelineGroupsTests
{
    [Fact]
    public void Groups_by_month_newest_first_whatever_the_input_order()
    {
        var groups = TimelineGroups.ByMonth(
        [
            new(Ids.Of(1), "photo", new DateTime(2019, 7, 14), null),
            new(Ids.Of(2), "photo", null, null),
            new(Ids.Of(3), "photo", new DateTime(2019, 7, 20), null),
            new(Ids.Of(4), "photo", new DateTime(2020, 1, 1), null),
        ]);

        Assert.Equal(["janeiro de 2020", "julho de 2019", "Sem data"], groups.Select(g => g.Title));
        Assert.Equal([Ids.Of(3), Ids.Of(1)], groups[1].Items.Select(i => i.Id));
    }

    [Fact]
    public void No_sem_data_group_when_every_asset_has_a_date()
    {
        var groups = TimelineGroups.ByMonth([new(Ids.Of(1), "photo", new DateTime(2024, 3, 9), null)]);

        Assert.Equal(["março de 2024"], groups.Select(g => g.Title));
    }

    [Fact]
    public void Formats_a_date_and_time_in_portuguese()
    {
        Assert.Equal("14 de julho de 2019, 10:32", PortugueseDates.DateAndTime(new DateTime(2019, 7, 14, 10, 32, 5)));
    }
}
