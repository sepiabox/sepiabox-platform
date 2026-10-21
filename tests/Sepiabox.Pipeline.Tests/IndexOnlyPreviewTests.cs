using ImageMagick;
using Sepiabox.Pipeline.Imaging;

namespace Sepiabox.Pipeline.Tests;

/// <summary>The larger photo preview of an index-only work folder (S-063): 2048 px on the long side, never upscaled.</summary>
public class IndexOnlyPreviewTests
{
    private const string Folder = "derivatives/abc";

    [Fact]
    public void Makes_a_2048_preview_instead_of_the_1280_one()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("photo.jpg", TestPhotos.Sized(4000, 3000));

        var derivatives = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.IndexOnlyPhotoSizes);

        Assert.Equal([("thumb-256", 256, 192), ("preview-2048", 2048, 1536)], derivatives.Select(d => (d.Name, d.Width, d.Height)));
        Assert.All(derivatives, d => Assert.Equal("image/webp", d.ContentType));
        Assert.DoesNotContain(derivatives, d => d.Name == "preview-1280");
    }

    [Fact]
    public void Never_upscales_a_small_photo()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("photo.jpg", TestPhotos.Sized(1000, 750));

        var derivatives = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.IndexOnlyPhotoSizes);

        var preview = Assert.Single(derivatives, d => d.Name == "preview-2048");
        Assert.Equal((1000, 750), (preview.Width, preview.Height));
        using var written = new MagickImage(Path.Combine(temp.Path, preview.Path));
        Assert.Equal((1000u, 750u), (written.Width, written.Height));
    }

    [Theory]
    [InlineData(3000, 4000, 1536, 2048)]
    [InlineData(2048, 2048, 2048, 2048)]
    [InlineData(2049, 1000, 2048, 1000)]
    public void Fits_the_long_side_in_either_orientation(int width, int height, int expectedWidth, int expectedHeight)
    {
        using var temp = new TempFolder();
        var photo = temp.Write("photo.jpg", TestPhotos.Sized(width, height));

        var derivatives = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.IndexOnlyPhotoSizes);

        var preview = Assert.Single(derivatives, d => d.Name == "preview-2048");
        Assert.Equal((expectedWidth, expectedHeight), (preview.Width, preview.Height));
    }

    [Fact]
    public void Applies_the_orientation_to_the_preview()
    {
        using var temp = new TempFolder();
        // Stored 3000x2000 and turned upright by orientation 6: 2000x3000, so 1365x2048.
        var photo = temp.Write("photo.jpg", TestPhotos.Sized(3000, 2000, orientation: 6));

        var derivatives = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.IndexOnlyPhotoSizes);

        var preview = Assert.Single(derivatives, d => d.Name == "preview-2048");
        Assert.Equal((1365, 2048), (preview.Width, preview.Height));
    }

    [Fact]
    public void Uses_the_same_quality_as_the_1280_preview()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("photo.jpg", TestPhotos.Sized(3000, 2000));
        var derivatives = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.IndexOnlyPhotoSizes);

        using var original = new MagickImage(photo);
        original.AutoOrient();
        original.Strip();
        original.Resize(new MagickGeometry(2048, 2048) { Greater = true });
        original.Quality = ThumbnailGenerator.Quality;
        var expected = Path.Combine(temp.Path, "expected.webp");
        original.Write(expected, MagickFormat.WebP);

        Assert.Equal(File.ReadAllBytes(expected), File.ReadAllBytes(Path.Combine(temp.Path, derivatives.Single(d => d.Name == "preview-2048").Path)));
    }
}
