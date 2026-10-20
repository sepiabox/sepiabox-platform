using ImageMagick;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Imaging;

/// <param name="Name">Derivative name, also the file name without extension.</param>
/// <param name="LongestSide">Maximum width or height in pixels.</param>
public sealed record ThumbnailSize(string Name, int LongestSide);

/// <summary>
/// Turns a photo into the small WebP files the web shows instead of the original (D-015).
/// Orientation is applied to the pixels and all metadata, GPS included, is removed.
/// </summary>
public static class ThumbnailGenerator
{
    public const string ContentType = "image/webp";
    public const int Quality = 80;

    /// <summary>The large preview, which is also what the CLIP embedding is computed from (S-015).</summary>
    public const string PreviewName = "preview-1280";

    public static readonly IReadOnlyList<ThumbnailSize> PhotoSizes =
    [
        new("thumb-256", 256),
        new(PreviewName, 1280),
    ];

    /// <summary>
    /// Writes one WebP per size into <paramref name="workFolder"/>/<paramref name="relativeFolder"/>.
    /// Throws <see cref="MagickException"/> when the image cannot be decoded.
    /// </summary>
    public static IReadOnlyList<ManifestDerivative> Generate(
        string sourcePath, string workFolder, string relativeFolder, IReadOnlyList<ThumbnailSize> sizes)
    {
        using var stream = MediaFile.OpenRead(sourcePath);
        // No format hint and only the first image: a Canon CR2 is then read as a TIFF, giving the camera's
        // full-size JPEG in IFD0 instead of a slow demosaic of the sensor data (D-045).
        using var original = new MagickImage(stream);
        original.AutoOrient();
        original.Strip();

        Directory.CreateDirectory(Path.Combine(workFolder, relativeFolder));
        var derivatives = new List<ManifestDerivative>(sizes.Count);
        foreach (var size in sizes)
        {
            using var image = original.Clone();
            var side = (uint)size.LongestSide;
            // Greater: only shrink, so a small photo is never blown up.
            image.Resize(new MagickGeometry(side, side) { Greater = true });
            image.Quality = Quality;

            var relativePath = $"{relativeFolder}/{size.Name}.webp";
            Write(image, Path.Combine(workFolder, relativePath));
            derivatives.Add(new ManifestDerivative(size.Name, relativePath, (int)image.Width, (int)image.Height, ContentType));
        }

        return derivatives;
    }

    // Written to a temporary file first so an interrupted run never leaves half an image.
    private static void Write(IMagickImage image, string path)
    {
        var temporary = path + ".tmp";
        image.Write(temporary, MagickFormat.WebP);
        File.Move(temporary, path, overwrite: true);
    }
}
