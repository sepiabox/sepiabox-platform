using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class CaptureDateTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0);
    private static readonly FixedClock s_clock = new(Now);

    [Fact]
    public void Keeps_the_metadata_date_when_the_folder_names_another_day() =>
        Assert.Equal(
            new CaptureDate(new DateTime(2019, 7, 14, 16, 30, 5), CaptureDateSource.Metadata),
            CaptureDate.Resolve(new DateTime(2019, 7, 14, 16, 30, 5), "2005-07-14/IMG_20120304_101112.jpg", s_clock));

    [Fact]
    public void Uses_the_folder_when_the_metadata_date_is_before_1990() =>
        Assert.Equal(
            new CaptureDate(new DateTime(2005, 7, 14), CaptureDateSource.Folder),
            CaptureDate.Resolve(new DateTime(1980, 1, 1), "2005-07-14/photo.jpg", s_clock));

    [Fact]
    public void Uses_the_file_name_when_the_metadata_date_is_in_the_future() =>
        Assert.Equal(
            new CaptureDate(new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName),
            CaptureDate.Resolve(Now.AddDays(3), "VID_20120304_101112.mp4", s_clock));

    [Fact]
    public void Uses_the_path_when_there_is_no_metadata_date() =>
        Assert.Equal(
            new CaptureDate(new DateTime(2011, 9, 25, 11, 4, 25), CaptureDateSource.FileName),
            CaptureDate.Resolve(null, @"E:\Life\2011-09-25\MNU_110425_d8b2.MTS", s_clock));

    [Fact]
    public void Returns_nothing_when_neither_the_metadata_nor_the_path_has_a_date() =>
        Assert.Null(CaptureDate.Resolve(null, "2011-02-30/clip.mod", s_clock));

    [Fact]
    public void Falls_back_to_the_folder_when_the_file_name_date_is_implausible() =>
        Assert.Equal(
            new CaptureDate(new DateTime(2005, 7, 14), CaptureDateSource.Folder),
            CaptureDate.Resolve(null, "2005-07-14/IMG_19800101_000000.jpg", s_clock));

    [Fact]
    public void Returns_nothing_when_the_only_path_date_is_in_the_future() =>
        Assert.Null(CaptureDate.Resolve(null, "2099-01-01/clip.mod", s_clock));

    [Theory]
    [InlineData("1989-12-31T23:59:59", false)]
    [InlineData("1990-01-01T00:00:00", true)]
    [InlineData("2026-10-21T12:00:00", true)]
    [InlineData("2026-10-21T12:00:01", false)]
    public void Accepts_dates_from_1990_to_one_day_after_now_when_checking_plausibility(string value, bool plausible) =>
        Assert.Equal(plausible, CaptureDate.IsPlausible(DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture), s_clock));

    [Fact]
    public void Measures_now_on_the_clock_given_when_checking_plausibility()
    {
        var later = new FixedClock(new DateTime(2030, 1, 1));

        Assert.False(CaptureDate.IsPlausible(new DateTime(2029, 6, 1), s_clock));
        Assert.True(CaptureDate.IsPlausible(new DateTime(2029, 6, 1), later));
    }
}
