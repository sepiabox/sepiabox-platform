using Sepiabox.Pipeline.Media;
using Sepiabox.Tests.Shared;

namespace Sepiabox.Pipeline.Tests;

public class MetadataReaderTests
{
    [Fact]
    public void Reads_date_place_camera_and_size_from_a_phone_photo()
    {
        using var folder = new TempFolder();
        var path = folder.Write("porto.jpg", TestPhotos.Porto());

        var metadata = MetadataReader.Read(path);

        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 5), metadata.CapturedAt);
        Assert.Equal(new GeoPoint(41.18, -8.69), metadata.Location);
        Assert.Equal(new CameraInfo("Apple", "iPhone 8"), metadata.Camera);
        Assert.Equal(64, metadata.Width);
        Assert.Equal(48, metadata.Height);
        Assert.Null(metadata.DurationSeconds);
    }

    [Fact]
    public void Reads_only_the_size_when_a_photo_has_no_metadata()
    {
        using var folder = new TempFolder();
        var path = folder.Write("plain.png", TestPhotos.Plain());

        var metadata = MetadataReader.Read(path);

        Assert.Equal(MediaMetadata.Empty with { Width = 32, Height = 32 }, metadata);
    }

    [Fact]
    public void Returns_empty_metadata_for_a_file_that_only_has_a_photo_extension()
    {
        using var folder = new TempFolder();
        var path = folder.Write("fake.jpg", "not really a photo"u8.ToArray());

        Assert.Equal(MediaMetadata.Empty, MetadataReader.Read(path));
    }
}
