using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.QuickTime;

namespace Sepiabox.Pipeline.Media;

public sealed record GeoPoint(double Latitude, double Longitude);

public sealed record CameraInfo(string? Make, string? Model);

/// <param name="RawCapturedAt">
/// The capture date as the file writes it, unfiltered: a camera with a reset clock gives 1980-01-01. Never record
/// it as is; resolve it with <see cref="CaptureDate.Resolve"/>, which also falls back to the file's path (D-046).
/// </param>
/// <param name="Location">
/// Where it was taken: EXIF GPS for a photo, the QuickTime ISO 6709 position for a phone video. Videos whose position
/// MetadataExtractor does not expose get it from ffprobe when <c>process</c> probes them (<see cref="Video.VideoProbe"/>).
/// </param>
/// <param name="Camera">Make and model.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="DurationSeconds">Length of a video.</param>
public sealed record MediaMetadata(
    DateTime? RawCapturedAt,
    GeoPoint? Location,
    CameraInfo? Camera,
    int? Width,
    int? Height,
    double? DurationSeconds)
{
    public static readonly MediaMetadata Empty = new(null, null, null, null, null, null);
}

/// <summary>
/// Reads what a photo or video says about itself: when and where it was taken, with which camera,
/// and its size. Missing fields are null. I/O errors are not caught: a file that cannot be read is a problem to report.
/// </summary>
public static class MetadataReader
{
    public static MediaMetadata Read(string path)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            using var stream = MediaFile.OpenRead(path);
            directories = ImageMetadataReader.ReadMetadata(stream);
        }
        catch (ImageProcessingException)
        {
            return MediaMetadata.Empty;
        }

        var (width, height) = ReadDimensions(directories);
        return new MediaMetadata(
            CaptureDateReader.FromDirectories(directories),
            ReadLocation(directories),
            ReadCamera(directories),
            width,
            height,
            ReadDuration(directories));
    }

    private static GeoPoint? ReadLocation(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gps is not null && gps.TryGetGeoLocation(out var location) && !location.IsZero)
        {
            return new GeoPoint(Math.Round(location.Latitude, 6), Math.Round(location.Longitude, 6));
        }

        // Both the user-data ©xyz atom and the com.apple.quicktime.location.ISO6709 key land in this tag.
        return directories.OfType<QuickTimeMetadataHeaderDirectory>()
            .Select(quickTime => Iso6709.Parse(quickTime.GetString(QuickTimeMetadataHeaderDirectory.TagGpsLocation)))
            .FirstOrDefault(point => point is not null);
    }

    private static CameraInfo? ReadCamera(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var make = NullIfEmpty(ifd0?.GetString(ExifDirectoryBase.TagMake));
        var model = NullIfEmpty(ifd0?.GetString(ExifDirectoryBase.TagModel));
        return make is null && model is null ? null : new CameraInfo(make, model);
    }

    private static (int? Width, int? Height) ReadDimensions(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        // The image's own header is more reliable than EXIF, which editors often leave stale.
        (MetadataExtractor.Directory? Directory, int WidthTag, int HeightTag)[] sources =
        [
            (directories.OfType<JpegDirectory>().FirstOrDefault(), JpegDirectory.TagImageWidth, JpegDirectory.TagImageHeight),
            (directories.OfType<PngDirectory>().FirstOrDefault(), PngDirectory.TagImageWidth, PngDirectory.TagImageHeight),
            (directories.OfType<ExifSubIfdDirectory>().FirstOrDefault(), ExifDirectoryBase.TagExifImageWidth, ExifDirectoryBase.TagExifImageHeight),
        ];

        foreach (var (directory, widthTag, heightTag) in sources)
        {
            if (directory is not null
                && directory.TryGetInt32(widthTag, out var width) && width > 0
                && directory.TryGetInt32(heightTag, out var height) && height > 0)
            {
                return (width, height);
            }
        }

        // A movie has one track header per track; audio tracks report 0×0.
        foreach (var track in directories.OfType<QuickTimeTrackHeaderDirectory>())
        {
            if (track.TryGetInt32(QuickTimeTrackHeaderDirectory.TagWidth, out var width) && width > 0
                && track.TryGetInt32(QuickTimeTrackHeaderDirectory.TagHeight, out var height) && height > 0)
            {
                return (width, height);
            }
        }

        return (null, null);
    }

    private static double? ReadDuration(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var movie = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
        return movie?.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) is TimeSpan duration && duration > TimeSpan.Zero
            ? Math.Round(duration.TotalSeconds, 3)
            : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
