using ImageMagick;
using Sepiabox.Search.Clip;

namespace Sepiabox.Search.Tests;

public sealed class ClipPreprocessorTests
{
    private const int Plane = ClipPreprocessor.Size * ClipPreprocessor.Size;

    [Fact]
    public void Tensor_is_channel_first_and_normalized_with_the_clip_mean_and_deviation()
    {
        // Every pixel pure red: R = 255, G = B = 0.
        var rgb = new byte[Plane * 3];
        for (var i = 0; i < Plane; i++)
        {
            rgb[i * 3] = 255;
        }

        var tensor = ClipPreprocessor.ToTensor(rgb);

        Assert.Equal(Plane * 3, tensor.Length);
        Assert.Equal((1f - 0.48145466f) / 0.26862954f, tensor[0], 5);
        Assert.Equal((1f - 0.48145466f) / 0.26862954f, tensor[Plane - 1], 5);
        Assert.Equal((0f - 0.4578275f) / 0.26130258f, tensor[Plane], 5);
        Assert.Equal((0f - 0.40821073f) / 0.27577711f, tensor[(2 * Plane) + 7], 5);
    }

    [Fact]
    public void Tensor_rejects_input_of_the_wrong_size()
    {
        Assert.Throws<ArgumentException>(() => ClipPreprocessor.ToTensor(new byte[12]));
    }

    [Theory]
    [InlineData(600, 200)]
    [InlineData(200, 600)]
    [InlineData(224, 224)]
    [InlineData(100, 50)]
    public void Resize_and_crop_gives_a_224_square_whatever_the_shape(int width, int height)
    {
        using var image = new MagickImage(MagickColors.Orange, (uint)width, (uint)height);

        var rgb = ClipPreprocessor.ResizeAndCrop(image);

        Assert.Equal(Plane * 3, rgb.Length);
    }

    [Fact]
    public void Resize_and_crop_keeps_the_centre_of_a_wide_image()
    {
        // Three vertical bands, red | lime | blue: after the shorter side becomes 224 the crop is the lime band.
        using var image = new MagickImage(MagickColors.Red, 600, 200);
        image.Draw(new ImageMagick.Drawing.Drawables()
            .FillColor(MagickColors.Lime).Rectangle(200, 0, 399, 199)
            .FillColor(MagickColors.Blue).Rectangle(400, 0, 599, 199));

        var rgb = ClipPreprocessor.ResizeAndCrop(image);

        foreach (var pixel in new[] { 0, ClipPreprocessor.Size - 1, Plane - 1 })
        {
            Assert.True(rgb[pixel * 3] < 40, $"red at pixel {pixel} was {rgb[pixel * 3]}");
            Assert.True(rgb[(pixel * 3) + 1] > 215, $"green at pixel {pixel} was {rgb[(pixel * 3) + 1]}");
            Assert.True(rgb[(pixel * 3) + 2] < 40, $"blue at pixel {pixel} was {rgb[(pixel * 3) + 2]}");
        }
    }

    [Fact]
    public void Transparent_areas_become_white()
    {
        using var image = new MagickImage(MagickColors.Transparent, 300, 300);

        var rgb = ClipPreprocessor.ResizeAndCrop(image);

        Assert.All(rgb, value => Assert.Equal(255, value));
    }

    // Hugging Face's CLIPImageProcessor truncates the longer side (int(224 * long / short)), the reference
    // the golden test compares with; rounding would make 403×301 one pixel wider than the reference.
    [Theory]
    [InlineData(403, 301, 299, 224)]
    [InlineData(257, 389, 224, 339)]
    [InlineData(1280, 960, 298, 224)]
    [InlineData(224, 500, 224, 500)]
    public void Resized_size_truncates_the_longer_side_like_the_reference_processor(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(((uint)expectedWidth, (uint)expectedHeight), ClipPreprocessor.ResizedSize((uint)width, (uint)height));
    }
}
