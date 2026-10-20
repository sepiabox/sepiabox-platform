using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class TimelineGroupsTests
{
    [Fact]
    public void Groups_by_month_newest_first_whatever_the_input_order()
    {
        var groups = TimelineGroups.ByMonth(
        [
            new(Ids.Of(1), AssetKinds.Photo, new DateTime(2019, 7, 14), false, DurationSeconds: null),
            new(Ids.Of(2), AssetKinds.Photo, null, false, DurationSeconds: null),
            new(Ids.Of(3), AssetKinds.Photo, new DateTime(2019, 7, 20), false, DurationSeconds: null),
            new(Ids.Of(4), AssetKinds.Photo, new DateTime(2020, 1, 1), false, DurationSeconds: null),
        ]);

        Assert.Equal([new DateOnly(2020, 1, 1), new DateOnly(2019, 7, 1), (DateOnly?)null], groups.Select(g => g.Month));
        Assert.Equal([Ids.Of(3), Ids.Of(1)], groups[1].Items.Select(i => i.Id));
    }

    [Fact]
    public void No_undated_group_when_every_asset_has_a_date()
    {
        var groups = TimelineGroups.ByMonth([new(Ids.Of(1), AssetKinds.Photo, new DateTime(2024, 3, 9), false, DurationSeconds: null)]);

        Assert.Equal([new DateOnly(2024, 3, 1)], groups.Select(g => g.Month));
    }

    [Theory]
    [InlineData(1, LibraryQueries.PageSize + 1, true)]
    [InlineData(1, LibraryQueries.PageSize, false)]
    [InlineData(LibraryQueries.MaxPage, LibraryQueries.PageSize + 1, false)]
    public void There_is_no_next_page_after_the_last_allowed_one(int page, int rowsFetched, bool expected)
    {
        Assert.Equal(expected, LibraryQueries.HasNextPage(page, rowsFetched));
    }

    [Theory]
    [InlineData("2019/IMG_0001.HEIC", "image/heic", "IMG_0001.HEIC")]
    [InlineData(null, "image/jpeg", "abc.jpg")]
    [InlineData(null, "video/mp4", "abc.mp4")]
    [InlineData(null, "video/mpeg", "abc.mpg")]
    [InlineData(null, "video/mp2t", "abc.mts")]
    [InlineData(null, "video/dv", "abc.dv")]
    [InlineData(null, "video/x-dv", "abc.dv")]
    [InlineData(null, "application/x-unknown", "abc")]
    [InlineData("2019/", "image/png", "abc.png")]
    public void A_download_is_named_after_its_source_or_its_id_and_type(string? path, string contentType, string expected)
    {
        Assert.Equal(expected, LibraryQueries.DownloadName(path, "abc", contentType));
    }
}
