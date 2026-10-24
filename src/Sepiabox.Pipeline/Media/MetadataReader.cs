using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.QuickTime;

namespace Sepiabox.Pipeline.Media;

public sealed record GeoPoint(double Latitude, double Longitude)
{
    /// <summary>A place on the globe: finite, within -90..90 and -180..180. The manifest's schema refuses any other (D-127).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOnTheGlobe =>
        double.IsFinite(Latitude) && double.IsFinite(Longitude) && Math.Abs(Latitude) <= 90 && Math.Abs(Longitude) <= 180;
}

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
        // Opened outside the reading, so a path that cannot be opened is reported, never read as a file without metadata.
        using var stream = MediaFile.OpenRead(path);
        return Read(stream);
    }

    /// <summary>The same from a stream, such as a photo already read into memory (D-078).</summary>
    public static MediaMetadata Read(Stream stream)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(stream);
        }
        catch (Exception ex) when (IsMalformed(ex))
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

    // MetadataExtractor reports most damage as ImageProcessingException, but a malformed atom or segment can also
    // overflow a size, index outside a buffer or end early (a truncated file). Those are the file's content, so it is
    // processed without metadata; IOException and UnauthorizedAccessException are failures to read and still throw.
    private static bool IsMalformed(Exception ex) =>
        ex is ImageProcessingException or OverflowException or ArgumentException or IndexOutOfRangeException or EndOfStreamException;

    private static GeoPoint? ReadLocation(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        // A camera can write any number in the GPS block (latitude and longitude around -436774 were seen), and the manifest schema
        // refuses a place outside the globe: such a value is no place, and the photo goes on without one (D-127).
        if (gps is not null && gps.TryGetGeoLocation(out var location) && !location.IsZero
            && new GeoPoint(location.Latitude, location.Longitude).IsOnTheGlobe)
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
