using ImageMagick;
using Sepiabox.Search.Clip;

namespace Sepiabox.Search.Tests;

public sealed class RegionCropTests
{
    [Theory]
    [InlineData(1280, 960, 0.0, 0.0, 0.5, 0.5, 0, 0, 640, 480)]
    [InlineData(1280, 960, 0.25, 0.5, 0.25, 0.25, 320, 480, 320, 240)]
    // The far edge is the photo's edge: x + width is 1 plus a rounding error.
    [InlineData(1000, 800, 0.7, 0.6, 0.30000000000000004, 0.4, 700, 480, 300, 320)]
    // A fraction of a pixel is rounded outwards, so the drawn part is never lost.
    [InlineData(100, 100, 0.105, 0.105, 0.01, 0.01, 10, 10, 2, 2)]
    // Never less than one pixel each way.
    [InlineData(100, 100, 0.5, 0.5, 0.0001, 0.0001, 50, 50, 1, 1)]
    public void Gives_the_expected_pixel_box_for_a_relative_one(
        uint imageWidth, uint imageHeight, double x, double y, double w, double h, int left, int top, uint width, uint height)
    {
        Assert.Equal((left, top, width, height), RegionCrop.PixelBox(imageWidth, imageHeight, [x, y, w, h]));
    }

    [Fact]
    public void Cuts_the_region_out_of_the_image_with_the_expected_size_and_pixels()
    {
        using var image = new MagickImage(MagickColors.Blue, 400, 200);
        using (var red = new MagickImage(MagickColors.Red, 100, 100))
        {
            image.Composite(red, 100, 50, CompositeOperator.Over);
        }

        RegionCrop.Crop(image, [0.25, 0.25, 0.25, 0.5]);

        Assert.Equal((100u, 100u), (image.Width, image.Height));
        using var pixels = image.GetPixels();
        Assert.Equal(MagickColors.Red, pixels.GetPixel(50, 50).ToColor());
    }

    [Fact]
    public void Prepares_a_224_square_of_rgb_from_an_encoded_photo_whatever_the_shape_of_the_region()
    {
        using var image = new MagickImage(MagickColors.Orange, 640, 480);
        var bytes = image.ToByteArray(MagickFormat.WebP);

        var tall = RegionCrop.Prepare(bytes, [0.1, 0.1, 0.2, 0.8]);
        var wide = RegionCrop.Prepare(bytes, [0, 0, 1, 0.1]);

        Assert.Equal(ClipPreprocessor.Size * ClipPreprocessor.Size * 3, tall.Length);
        Assert.Equal(tall.Length, wide.Length);
    }

    [Fact]
    public void Refuses_a_preview_with_oversized_dimensions_before_decoding_it()
    {
        using var image = new MagickImage(MagickColors.White, RegionCrop.MaxSide + 1, 8);
        var bytes = image.ToByteArray(MagickFormat.WebP);

        Assert.Throws<InvalidOperationException>(() => RegionCrop.Prepare(bytes, [0, 0, 1, 1]));
    }

    [Fact]
    public void Reads_only_webp_whatever_the_bytes_claim_to_be()
    {
        using var image = new MagickImage(MagickColors.Orange, 64, 64);
        var png = image.ToByteArray(MagickFormat.Png);

        Assert.ThrowsAny<MagickException>(() => RegionCrop.Prepare(png, [0, 0, 1, 1]));
    }

    [Theory]
    [InlineData(new double[] { 0.1, 0.1, 0.5 })]
    [InlineData(new double[] { 0.1, 0.1, 0.5, 0.5, 0.5 })]
    [InlineData(new double[] { -0.1, 0.1, 0.5, 0.5 })]
    [InlineData(new double[] { 1.1, 0.1, 0.1, 0.5 })]
    [InlineData(new double[] { 0.1, 0.1, 0, 0.5 })]
    [InlineData(new double[] { 0.1, 0.1, 0.5, 0 })]
    [InlineData(new double[] { 0.1, 0.1, -0.5, 0.5 })]
    [InlineData(new double[] { 0.6, 0.1, 0.5, 0.5 })]
    [InlineData(new double[] { 0.1, 0.6, 0.5, 0.5 })]
    [InlineData(new double[] { double.NaN, 0.1, 0.5, 0.5 })]
    [InlineData(new double[] { 0.1, 0.1, double.PositiveInfinity, 0.5 })]
    public void Refuses_a_box_that_is_not_inside_the_photo_with_an_area(double[] box)
    {
        Assert.False(RegionCrop.IsValid(box));
        Assert.Throws<ArgumentException>(() => RegionCrop.PixelBox(100, 100, box));
    }

    [Fact]
    public void Refuses_a_missing_box()
    {
        Assert.False(RegionCrop.IsValid(null));
    }
}
