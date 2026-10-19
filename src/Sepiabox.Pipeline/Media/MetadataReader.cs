using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.QuickTime;

namespace Sepiabox.Pipeline.Media;

public sealed record GeoPoint(double Latitude, double Longitude);

public sealed record CameraInfo(string? Make, string? Model);

public sealed record MediaMetadata(
    DateTime? CapturedAt,
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
/// and its size. Missing fields are null. I/O errors are not caught, as in <see cref="CaptureDateReader"/>.
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
        if (gps is null || !gps.TryGetGeoLocation(out var location) || location.IsZero)
        {
            return null;
        }

        return new GeoPoint(Math.Round(location.Latitude, 6), Math.Round(location.Longitude, 6));
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
