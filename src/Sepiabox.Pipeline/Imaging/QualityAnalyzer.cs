using System.Globalization;
using ImageMagick;

namespace Sepiabox.Pipeline.Imaging;

/// <summary>How sharp a photo is and what it looks like, in 64 bits (S-027, D-098).</summary>
/// <param name="Sharpness">The variance of the Laplacian over the grayscale image at 512 px: the higher, the sharper.</param>
/// <param name="DHash">The difference hash, 16 lower-case hex characters: photos that look alike have hashes a few bits apart.</param>
public sealed record ImageQuality(double Sharpness, string DHash);

/// <summary>
/// Sharpness and a perceptual hash of a photo, from the image the pipeline already made for the web (S-027, D-098), so
/// the original is never read for them and a work folder processed before this existed gets them from its previews.
/// </summary>
public static class QualityAnalyzer
{
    /// <summary>The longest side, in pixels, the sharpness is measured at: one scale for every photo, whatever its size.</summary>
    public const int SharpnessSide = 512;

    /// <summary>A hash has 64 bits, 16 hex characters.</summary>
    public const int HashLength = 16;

    /// <summary>Reads the image at <paramref name="path"/> (a WebP preview, as the pipeline writes it).</summary>
    public static ImageQuality Analyze(string path)
    {
        using var image = new MagickImage(path);
        return Analyze(image);
    }

    /// <summary>Both measures of <paramref name="image"/>, which is not changed.</summary>
    public static ImageQuality Analyze(IMagickImage<byte> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new ImageQuality(Sharpness(image), DHash(image));
    }

    /// <summary>
    /// The variance of the Laplacian (the sum of a pixel's four neighbours minus four times the pixel) over the image in
    /// grayscale, at most <see cref="SharpnessSide"/> px on its longest side: a blurred image has few sharp edges, so
    /// the Laplacian is nearly flat and its variance low. A flat image is 0.
    /// </summary>
    public static double Sharpness(IMagickImage<byte> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var gray = Gray(image, 0, 0);
        var (width, height) = ((int)gray.Width, (int)gray.Height);
        var values = Luma(gray);
        if (width < 3 || height < 3)
        {
            return 0;
        }

        double sum = 0, sumOfSquares = 0;
        var count = 0L;
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                double laplacian = values[i - 1] + values[i + 1] + values[i - width] + values[i + width] - 4 * values[i];
                sum += laplacian;
                sumOfSquares += laplacian * laplacian;
                count++;
            }
        }

        var mean = sum / count;
        return Math.Max(0, sumOfSquares / count - mean * mean);
    }

    /// <summary>
    /// The difference hash: the image in grayscale squeezed to 9×8, one bit per pair of neighbours in a row (1 when the left
    /// one is brighter), 64 bits in all, written as 16 hex characters. Resizing the image changes few bits.
    /// </summary>
    public static string DHash(IMagickImage<byte> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var gray = Gray(image, 9, 8);
        var values = Luma(gray);
        ulong hash = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                hash <<= 1;
                if (values[y * 9 + x] > values[y * 9 + x + 1])
                {
                    hash |= 1;
                }
            }
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>How many bits differ between two hashes; throws when one is not 16 hex characters.</summary>
    public static int HammingDistance(string a, string b)
    {
        if (!IsHash(a) || !IsHash(b))
        {
            throw new ArgumentException("A hash is 16 lower-case hex characters.");
        }

        return System.Numerics.BitOperations.PopCount(
            ulong.Parse(a, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
            ^ ulong.Parse(b, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
    }

    public static bool IsHash(string? value) =>
        value is { Length: HashLength } && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    // A grayscale copy: exactly width × height when both are given (a hash ignores the aspect ratio), otherwise shrunk to
    // SharpnessSide on the longest side and never enlarged.
    private static MagickImage Gray(IMagickImage<byte> image, uint width, uint height)
    {
        var copy = (MagickImage)image.Clone();
        copy.HasAlpha = false;
        copy.Grayscale();
        copy.Resize(width > 0
            ? new MagickGeometry(width, height) { IgnoreAspectRatio = true }
            : new MagickGeometry(SharpnessSide, SharpnessSide) { Greater = true });
        return copy;
    }

    // One value per pixel, row by row (the first of the three channels of a gray image).
    private static byte[] Luma(MagickImage gray)
    {
        using var pixels = gray.GetPixels();
        var rgb = pixels.ToByteArray(PixelMapping.RGB) ?? throw new InvalidOperationException("The image has no pixels.");
        var luma = new byte[gray.Width * gray.Height];
        for (var i = 0; i < luma.Length; i++)
        {
            luma[i] = rgb[i * 3];
        }

        return luma;
    }
}
