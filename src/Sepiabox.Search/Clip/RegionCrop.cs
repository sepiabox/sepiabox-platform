using ImageMagick;

namespace Sepiabox.Search.Clip;

/// <summary>
/// A region of a photo for "photos like this part" (S-041, D-099): a box <c>[x, y, width, height]</c> relative to the
/// photo (0 to 1), cut from a derivative. Derivatives are upright (D-047), so a box drawn on the preview in the browser
/// means the same part on any of them.
/// </summary>
public static class RegionCrop
{
    /// <summary>Slack for a box drawn to the very edge, whose x + width is 1 plus a rounding error.</summary>
    private const double Epsilon = 1e-9;

    /// <summary>Whether <paramref name="box"/> is four finite numbers in 0 to 1, with a width and height above 0, inside the photo.</summary>
    public static bool IsValid(IReadOnlyList<double>? box) =>
        box is { Count: 4 }
        && box.All(double.IsFinite)
        && box[0] is >= 0 and <= 1 && box[1] is >= 0 and <= 1
        && box[2] > 0 && box[3] > 0
        && box[0] + box[2] <= 1 + Epsilon && box[1] + box[3] <= 1 + Epsilon;

    /// <summary>
    /// The region in pixels of an image <paramref name="imageWidth"/> × <paramref name="imageHeight"/>: the edges are
    /// rounded outwards, so the region never loses what was drawn, and it is at least one pixel each way.
    /// </summary>
    public static (int X, int Y, uint Width, uint Height) PixelBox(uint imageWidth, uint imageHeight, IReadOnlyList<double> box)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (!IsValid(box))
        {
            throw new ArgumentException("The box must be [x, y, width, height], relative to the photo, with a width and a height above 0.", nameof(box));
        }

        var left = Math.Min((int)Math.Floor(box[0] * imageWidth), (int)imageWidth - 1);
        var top = Math.Min((int)Math.Floor(box[1] * imageHeight), (int)imageHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling((box[0] + box[2]) * imageWidth - Epsilon), left + 1, (int)imageWidth);
        var bottom = Math.Clamp((int)Math.Ceiling((box[1] + box[3]) * imageHeight - Epsilon), top + 1, (int)imageHeight);
        return (left, top, (uint)(right - left), (uint)(bottom - top));
    }

    /// <summary>Cuts the region of <paramref name="image"/> out, in place.</summary>
    public static void Crop(MagickImage image, IReadOnlyList<double> box)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (x, y, width, height) = PixelBox(image.Width, image.Height, box);
        image.Crop(new MagickGeometry(x, y, width, height));
        image.ResetPage();
    }

    /// <summary>
    /// The model's input for the region of the encoded image <paramref name="photo"/>: the region cut out and then
    /// prepared as <see cref="ClipPreprocessor"/> prepares a whole photo (D-016), so it sits in the same space as the stored embeddings.
    /// </summary>
    public static byte[] Prepare(byte[] photo, IReadOnlyList<double> box)
    {
        ArgumentNullException.ThrowIfNull(photo);
        using var image = new MagickImage(photo);
        image.AutoOrient();
        Crop(image, box);
        return ClipPreprocessor.ResizeAndCrop(image);
    }
}
