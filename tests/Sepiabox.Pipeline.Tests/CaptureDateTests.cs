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

    [Fact]
    public void Keeps_a_folder_date_before_1990_when_a_person_named_the_folder() =>
        Assert.Equal(
            new CaptureDate(new DateTime(1978, 8, 15), CaptureDateSource.Folder),
            CaptureDate.Resolve(null, "Digitalizadas/1978-08-15/scan.jpg", s_clock));

    [Theory]
    [InlineData("1989-12-31T23:59:59", false)]
    [InlineData("1990-01-01T00:00:00", true)]
    [InlineData("2026-10-21T12:00:00", true)]
    [InlineData("2026-10-21T12:00:01", false)]
    public void Keeps_a_metadata_date_from_1990_to_one_day_after_now_when_resolving(string value, bool kept)
    {
        var taken = DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(kept ? new CaptureDate(taken, CaptureDateSource.Metadata) : null, CaptureDate.Resolve(taken, "Fotos/photo.jpg", s_clock));
    }

    public static TheoryData<string, CaptureDate?> PathBounds => new()
    {
        // A date the device wrote in the file name: from 1990.
        { "IMG_19891231_235959.jpg", null },
        { "IMG_19900101_000000.jpg", new(new DateTime(1990, 1, 1), CaptureDateSource.FileName) },
        { "IMG_20261021_120000.jpg", new(new DateTime(2026, 10, 21, 12, 0, 0), CaptureDateSource.FileName) },
        { "IMG_20261021_120001.jpg", null },
        // A day a person typed as the folder name, alone or with a camcorder's time: from 1900.
        { "1899-12-31/photo.jpg", null },
        { "1900-01-01/photo.jpg", new(new DateTime(1900, 1, 1), CaptureDateSource.Folder) },
        { "1899-12-31/MNU_101010.mod", null },
        { "1900-01-01/MNU_101010.mod", new(new DateTime(1900, 1, 1, 10, 10, 10), CaptureDateSource.FileName) },
        { "2026-10-21/photo.jpg", new(new DateTime(2026, 10, 21), CaptureDateSource.Folder) },
        { "2026-10-22/photo.jpg", null },
    };

    [Theory]
    [MemberData(nameof(PathBounds))]
    public void Keeps_a_path_date_only_within_its_plausibility_window_when_resolving(string path, CaptureDate? expected) =>
        Assert.Equal(expected, CaptureDate.Resolve(null, path, s_clock));

    [Fact]
    public void Measures_now_on_the_clock_given_when_resolving()
    {
        var later = new FixedClock(new DateTime(2030, 1, 1));

        Assert.Null(CaptureDate.Resolve(new DateTime(2029, 6, 1), "photo.jpg", s_clock));
        Assert.Equal(new CaptureDate(new DateTime(2029, 6, 1), CaptureDateSource.Metadata), CaptureDate.Resolve(new DateTime(2029, 6, 1), "photo.jpg", later));
    }

    public static TheoryData<CaptureDate?, CaptureDate?, CaptureDate?> Strongest => new()
    {
        { Folder(2008, 12, 24), FileName(2008, 12, 25), FileName(2008, 12, 25) },
        { FileName(2012, 3, 4), Metadata(2019, 7, 14), Metadata(2019, 7, 14) },
        { Folder(2008, 12, 24), Metadata(2019, 7, 14), Metadata(2019, 7, 14) },
        { Folder(2008, 12, 24), null, Folder(2008, 12, 24) },
        { Folder(2008, 12, 25), Folder(2008, 12, 24), Folder(2008, 12, 24) },
        { FileName(2011, 9, 26), FileName(2011, 9, 25), FileName(2011, 9, 25) },
        { null, null, null },
    };

    [Theory]
    [MemberData(nameof(Strongest))]
    public void Picks_the_same_date_in_either_order_when_comparing_two_dates(CaptureDate? first, CaptureDate? second, CaptureDate? expected)
    {
        Assert.Equal(expected, CaptureDate.Stronger(first, second));
        Assert.Equal(expected, CaptureDate.Stronger(second, first));
    }

    [Fact]
    public void Ranks_metadata_over_export_over_file_name_over_folder_when_comparing_sources()
    {
        Assert.Equal(
            [CaptureDateSource.Folder, CaptureDateSource.FileName, CaptureDateSource.Export, CaptureDateSource.Metadata],
            Enum.GetValues<CaptureDateSource>().OrderBy(CaptureDate.Rank));
        Assert.Equal(4, Enum.GetValues<CaptureDateSource>().Select(CaptureDate.Rank).Distinct().Count());
    }

    [Fact]
    public void Dates_a_camcorder_clip_before_1990_when_its_day_comes_from_the_folder() =>
        Assert.Equal(
            new CaptureDate(new DateTime(1985, 5, 1, 10, 10, 10), CaptureDateSource.FileName),
            CaptureDate.Resolve(null, "1985-05-01/MNU_101010.mod", s_clock));

    [Fact]
    public void Keeps_the_preferred_date_when_both_come_from_metadata() =>
        Assert.Equal(Metadata(2019, 7, 15), CaptureDate.Stronger(Metadata(2019, 7, 15), Metadata(2019, 7, 14)));

    private static CaptureDate Folder(int year, int month, int day) => new(new DateTime(year, month, day), CaptureDateSource.Folder);

    private static CaptureDate FileName(int year, int month, int day) => new(new DateTime(year, month, day, 10, 0, 0), CaptureDateSource.FileName);

    private static CaptureDate Metadata(int year, int month, int day) => new(new DateTime(year, month, day, 16, 30, 0), CaptureDateSource.Metadata);
}
