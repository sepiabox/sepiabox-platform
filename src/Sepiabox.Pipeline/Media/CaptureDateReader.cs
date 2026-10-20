using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Finds the capture date a photo or video records about itself, as written, in metadata that
/// <see cref="MetadataReader"/> has read: a camera with a reset clock gives dates such as 1980-01-01, which
/// <see cref="CaptureDate.Resolve"/> rejects (D-046). Callers that record a date go through
/// <see cref="CaptureDate.Resolve"/>, never this alone.
/// </summary>
public static class CaptureDateReader
{
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
