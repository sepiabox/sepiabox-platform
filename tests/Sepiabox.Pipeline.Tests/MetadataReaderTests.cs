using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class MetadataReaderTests
{
    [Fact]
    public void Reads_date_place_camera_and_size_from_a_phone_photo()
    {
        using var folder = new TempFolder();
        var path = folder.Write("porto.jpg", TestPhotos.Porto());

        var metadata = MetadataReader.Read(path);

        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 5), metadata.RawCapturedAt);
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

    [Fact]
    public void Keeps_the_exif_gps_location_of_a_photo()
    {
        using var folder = new TempFolder();
        var path = folder.Write("porto.jpg", TestPhotos.Porto());

        Assert.Equal(new GeoPoint(41.18, -8.69), MetadataReader.Read(path).Location);
    }

    [Fact]
    public void Reads_the_location_of_a_video_from_the_user_data_atom()
    {
        using var folder = new TempFolder();
        var path = folder.Write("android.mp4", TestQuickTime.Movie(userData: "+38.7223-009.1393+010.000/"));

        var metadata = MetadataReader.Read(path);

        Assert.Equal(new GeoPoint(38.7223, -9.1393), metadata.Location);
        Assert.Equal(2, metadata.DurationSeconds);
    }

    [Fact]
    public void Reads_the_location_of_a_video_from_the_apple_metadata_key()
    {
        using var folder = new TempFolder();
        var path = folder.Write("IMG_0001.MOV", TestQuickTime.Movie(appleKey: "-33.8568+151.2153+005.000/"));

        Assert.Equal(new GeoPoint(-33.8568, 151.2153), MetadataReader.Read(path).Location);
    }

    [Theory]
    [InlineData("+00.0000+000.0000/")]
    [InlineData("+95.0000+000.0000/")]
    [InlineData("+3843.34-00908.36/")]
    public void Has_no_location_when_a_video_records_an_unusable_one(string iso6709)
    {
        using var folder = new TempFolder();
        var path = folder.Write("clip.mov", TestQuickTime.Movie(userData: iso6709, appleKey: iso6709));

        Assert.Null(MetadataReader.Read(path).Location);
    }

    [Fact]
    public void Has_no_location_when_a_video_records_none()
    {
        using var folder = new TempFolder();
        var path = folder.Write("clip.mov", TestQuickTime.Movie());

        Assert.Null(MetadataReader.Read(path).Location);
    }

    [Fact]
    public void Reads_the_capture_date_from_a_real_camera_jpeg() =>
        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 5), MetadataReader.Read(TestData("taken-2019-07-14.jpg")).RawCapturedAt);

    [Fact]
    public void Returns_no_capture_date_when_a_jpeg_has_no_exif() =>
        Assert.Null(MetadataReader.Read(TestData("no-exif.jpg")).RawCapturedAt);

    [Fact]
    public void Returns_the_capture_date_as_written_when_the_camera_clock_was_reset()
    {
        using var folder = new TempFolder();
        var path = folder.Write("reset.jpg", TestPhotos.Taken("1980:01:01 00:00:00"));

        // Plausibility is decided with the path fallback, in CaptureDate.Resolve (D-046).
        Assert.Equal(new DateTime(1980, 1, 1), MetadataReader.Read(path).RawCapturedAt);
    }

    [Fact]
    public void Throws_when_the_file_is_gone()
    {
        using var folder = new TempFolder();

        Assert.Throws<FileNotFoundException>(() => MetadataReader.Read(Path.Combine(folder.Path, "gone.jpg")));
    }

    [Fact]
    public void Throws_when_the_path_is_a_folder()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "album.jpg");
        Directory.CreateDirectory(path);

        // Opening a folder as a file is refused on every OS; the caller reports it as a problem.
        var error = Record.Exception(() => MetadataReader.Read(path));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
    }

    private static string TestData(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);
}
