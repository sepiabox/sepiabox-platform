using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Reads the capture date a photo or video records about itself, as written: a camera with a reset clock gives
/// dates such as 1980-01-01, which <see cref="CaptureDate.Resolve"/> rejects (D-046). Callers that record a date go
/// through <see cref="CaptureDate.Resolve"/>, never this reader alone.
/// </summary>
public static class CaptureDateReader
{
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

        return FromDirectories(directories);
    }

    /// <summary>Capture date from metadata that was already read, so a file is parsed only once.</summary>
    public static DateTime? FromDirectories(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (exif is not null && exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken))
        {
            return taken;
        }

        var movie = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
        if (movie is not null && movie.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var created))
        {
            return created;
        }

        return null;
    }
}
