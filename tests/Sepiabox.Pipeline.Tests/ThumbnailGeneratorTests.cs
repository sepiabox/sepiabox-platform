using ImageMagick;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Tests.Shared;

namespace Sepiabox.Pipeline.Tests;

public class ThumbnailGeneratorTests
{
    private const string Folder = "derivatives/abc";

    [Fact]
    public void Fits_each_size_to_its_longest_side_keeping_the_aspect_ratio()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("photo.jpg", TestPhotos.Sized(4000, 3000));

        var derivatives = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes);

        Assert.Equal(
            [("thumb-256", 256, 192), ("preview-1280", 1280, 960)],
            derivatives.Select(d => (d.Name, d.Width, d.Height)));
        foreach (var derivative in derivatives)
        {
            Assert.Equal($"{Folder}/{derivative.Name}.webp", derivative.Path);
            Assert.Equal("image/webp", derivative.ContentType);
            using var written = new MagickImage(Path.Combine(temp.Path, derivative.Path));
            Assert.Equal(MagickFormat.WebP, written.Format);
            Assert.Equal(((uint)derivative.Width, (uint)derivative.Height), (written.Width, written.Height));
        }
    }

    [Fact]
    public void Applies_the_exif_orientation_to_the_pixels()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("sideways.jpg", TestPhotos.Sized(400, 300, orientation: 6));

        var thumb = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes)[0];

        Assert.True(thumb.Height > thumb.Width, $"Expected portrait, got {thumb.Width}×{thumb.Height}.");
        Assert.Equal((192, 256), (thumb.Width, thumb.Height));
    }

    [Fact]
    public void Removes_all_metadata_including_gps()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("porto.jpg", TestPhotos.Porto());

        var thumb = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes)[0];

        using var written = new MagickImage(Path.Combine(temp.Path, thumb.Path));
        Assert.Null(written.GetExifProfile());
        Assert.Null(written.GetXmpProfile());
    }

    [Fact]
    public void Does_not_upscale_a_photo_smaller_than_the_target()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("small.jpg", TestPhotos.Sized(200, 100));

        var derivatives = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes);

        Assert.All(derivatives, d => Assert.Equal((200, 100), (d.Width, d.Height)));
    }

    [Fact]
    public void Throws_a_magick_exception_for_a_corrupted_image()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("broken.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46]);

        Assert.ThrowsAny<MagickException>(() => ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, Folder)) && Directory.EnumerateFiles(Path.Combine(temp.Path, Folder)).Any());
    }
}
