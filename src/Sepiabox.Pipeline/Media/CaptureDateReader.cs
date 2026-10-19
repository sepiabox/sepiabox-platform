using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace Sepiabox.Pipeline.Media;

public static class CaptureDateReader
{
    // Cameras whose clock was never set write dates like 0000:00:00 or 1904 (the QuickTime epoch).
    private static readonly DateTime EarliestPlausible = new(1990, 1, 1);

    /// <summary>
    /// Returns when a photo or video was taken, from its own metadata, or null when the file
    /// does not say. I/O errors are not caught: a file that cannot be read is a problem to report.
    /// </summary>
    public static DateTime? TryRead(string path)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(path);
        }
        catch (ImageProcessingException)
        {
            // Unknown format or no metadata at all.
            return null;
        }

        var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (exif is not null && exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken))
        {
            return Plausible(taken);
        }

        var movie = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
        if (movie is not null && movie.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var created))
        {
            return Plausible(created);
        }

        return null;
    }

    private static DateTime? Plausible(DateTime value) =>
        value >= EarliestPlausible && value <= DateTime.Now.AddDays(1) ? value : null;
}
