using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class CaptureDateReaderTests
{
    [Fact]
    public void Reads_when_a_photo_was_taken() =>
        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 5), CaptureDateReader.TryRead(TestData("taken-2019-07-14.jpg")));

    [Fact]
    public void Returns_null_when_a_photo_has_no_date() =>
        Assert.Null(CaptureDateReader.TryRead(TestData("no-exif.jpg")));

    [Fact]
    public void Returns_null_for_a_file_that_only_has_a_photo_extension()
    {
        using var folder = new TempFolder();
        var path = folder.Write("fake.jpg", "not really a photo"u8.ToArray());

        Assert.Null(CaptureDateReader.TryRead(path));
    }

    private static string TestData(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);
}
