using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class PathDateTests
{
    public static TheoryData<string, DateTime, CaptureDateSource> Dated => new()
    {
        // Date and time in the file name, with any prefix and either separator.
        { "IMG_20120304_101112.jpg", new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName },
        { "VID_20120304_101112.mp4", new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName },
        { "20120304_101112.mp4", new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName },
        { "PXL_20230115_143022123.jpg", new DateTime(2023, 1, 15, 14, 30, 22), CaptureDateSource.FileName },
        { "Screenshot_20120304-101112.png", new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName },
        { "/media/hd/Fotos/IMG_20120304_101112.jpg", new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName },
        // The file name's date wins over the folder's.
        { "2005-07-14/IMG_20120304_101112.jpg", new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName },
        // Camcorder clip: the day from the folder, the time from the file name.
        { @"E:\Life\2011-09-25\MNU_110425_d8b2.MTS", new DateTime(2011, 9, 25, 11, 4, 25), CaptureDateSource.FileName },
        { "/media/hd/Life/2011-09-25/MNU_110425_d8b2.MTS", new DateTime(2011, 9, 25, 11, 4, 25), CaptureDateSource.FileName },
        { "2011-09-25/mnu_110425.mts", new DateTime(2011, 9, 25, 11, 4, 25), CaptureDateSource.FileName },
        // An impossible time in the file name leaves the folder's day.
        { "2011-09-25/MNU_256100_d8b2.MTS", new DateTime(2011, 9, 25), CaptureDateSource.Folder },
        // The folder alone: that day at midnight.
        { @"2008-12-24\clip.mod", new DateTime(2008, 12, 24), CaptureDateSource.Folder },
        { "2008-12-24/clip.mod", new DateTime(2008, 12, 24), CaptureDateSource.Folder },
        { @"E:\Life\2008-12-24\clip.mod", new DateTime(2008, 12, 24), CaptureDateSource.Folder },
        { "2009-05-01/IMG-20090501-WA0001.jpg", new DateTime(2009, 5, 1), CaptureDateSource.Folder },
        { "2009-05-01/IMG_20090230_101112.jpg", new DateTime(2009, 5, 1), CaptureDateSource.Folder },
    };

    [Theory]
    [MemberData(nameof(Dated))]
    public void Reads_the_date_when_the_path_has_one(string path, DateTime expected, CaptureDateSource source) =>
        Assert.Equal(new CaptureDate(expected, source), PathDate.TryParse(path));

    [Theory]
    [InlineData("2011-02-30/clip.mod")]
    [InlineData(@"E:\Life\2011-02-30\clip.mod")]
    [InlineData("2011-13-01/clip.mod")]
    [InlineData("2011-9-25/clip.mod")]
    [InlineData("25-09-2011/clip.mod")]
    [InlineData("2011-09-25 Batizado/clip.mod")]
    [InlineData("IMG_20120230_101112.jpg")]
    [InlineData("IMG_20120304_256100.jpg")]
    [InlineData("IMG_120120304_101112.jpg")]
    [InlineData("IMG-20120304-WA0001.jpg")]
    [InlineData("MNU_110425_d8b2.MTS")]
    [InlineData("Fotos/MNU_110425_d8b2.MTS")]
    [InlineData("2011-09-25")]
    [InlineData("clip.mod")]
    [InlineData("")]
    [InlineData("/")]
    public void Returns_nothing_when_the_path_has_no_valid_date(string path) =>
        Assert.Null(PathDate.TryParse(path));

    [Theory]
    [InlineData("Life/2011-09-25/Camcorder/clip.mod")]
    [InlineData(@"E:\Life\2011-09-25\Camcorder\MNU_110425_d8b2.MTS")]
    public void Ignores_a_dated_folder_when_it_is_not_the_parent(string path) =>
        Assert.Null(PathDate.TryParse(path));

    [Fact]
    public void Lists_every_date_in_order_of_precedence_when_the_path_has_several() =>
        Assert.Equal(
            [
                new CaptureDate(new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName),
                new CaptureDate(new DateTime(2011, 9, 25), CaptureDateSource.Folder),
            ],
            PathDate.Candidates("2011-09-25/IMG_20120304_101112.jpg"));
}
