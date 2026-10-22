using ImageMagick;

namespace Sepiabox.Search.Clip;

/// <summary>
/// Turns an image into CLIP ViT-B/32's input, as OpenAI's preprocessing does: shorter side resized to 224
/// (bicubic), centre crop 224×224, RGB scaled to 0–1 and normalized per channel.
/// </summary>
public static class ClipPreprocessor
{
    public const int Size = 224;

    public static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    public static readonly float[] StandardDeviation = [0.26862954f, 0.26130258f, 0.27577711f];

    /// <summary>The model's input for <paramref name="path"/>: a [1, 3, 224, 224] tensor in channel-first order.</summary>
    public static float[] Prepare(string path)
    {
        using var image = new MagickImage(path);
        return ToTensor(ResizeAndCrop(image));
    }

    /// <summary>Resizes the shorter side to 224 and crops the centre: 224×224 interleaved RGB bytes.</summary>
    public static byte[] ResizeAndCrop(MagickImage image)
    {
        image.AutoOrient();
        // Transparent areas become white, like a photo printed on paper, rather than black.
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaAction.Remove);
        image.ColorSpace = ColorSpace.sRGB;

        var (width, height) = ResizedSize(image.Width, image.Height);
        // Catmull-Rom is the bicubic kernel (a = −0.5) the reference preprocessing uses.
        image.FilterType = FilterType.Catrom;
        image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
        // The offset rounds down, like the reference's (size - 224) // 2.
        image.Crop(new MagickGeometry((int)(width - Size) / 2, (int)(height - Size) / 2, Size, Size));
        image.ResetPage();

        using var pixels = image.GetPixels();
        return pixels.ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidOperationException("The image has no pixels.");
    }

    /// <summary>
    /// The size with the shorter side at 224. The longer side is truncated, not rounded, as Hugging Face's
    /// CLIPImageProcessor does (<c>int(224 * long / short)</c>), so the crop matches the reference pixel for pixel.
    /// </summary>
    public static (uint Width, uint Height) ResizedSize(uint width, uint height)
    {
        var shorter = Math.Min(width, height);
        var longer = (uint)((double)Size * Math.Max(width, height) / shorter);
        return width <= height ? (Size, longer) : (longer, Size);
    }

    /// <summary>Interleaved 224×224 RGB bytes to a normalized channel-first tensor.</summary>
    public static float[] ToTensor(ReadOnlySpan<byte> rgb)
    {
        const int Plane = Size * Size;
        if (rgb.Length != Plane * 3)
        {
            throw new ArgumentException($"Expected {Plane * 3} bytes of 224×224 RGB, got {rgb.Length}.", nameof(rgb));
        }

        var tensor = new float[Plane * 3];
        for (var pixel = 0; pixel < Plane; pixel++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                var value = rgb[(pixel * 3) + channel] / 255f;
                tensor[(channel * Plane) + pixel] = (value - Mean[channel]) / StandardDeviation[channel];
            }
        }

        return tensor;
    }
}
