using System.Text.RegularExpressions;

namespace Sepiabox.Pipeline.Media;

public static partial class MediaTypes
{
    private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".heic", ".heif", ".png", ".gif", ".webp", ".tif", ".tiff", ".bmp",
        // Camera raw formats
        ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".3gp", ".mts", ".m2ts", ".wmv", ".mpg", ".mpeg",
        // Camcorder and DVD formats (D-045)
        ".mod", ".tod", ".m2t", ".vob", ".dv",
    };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".bmp"] = "image/bmp",
        [".dng"] = "image/x-adobe-dng",
        [".cr2"] = "image/x-canon-cr2",
        [".cr3"] = "image/x-canon-cr3",
        [".nef"] = "image/x-nikon-nef",
        [".arw"] = "image/x-sony-arw",
        [".orf"] = "image/x-olympus-orf",
        [".rw2"] = "image/x-panasonic-rw2",
        [".raf"] = "image/x-fuji-raf",
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".m4v"] = "video/x-m4v",
        [".avi"] = "video/x-msvideo",
        [".mkv"] = "video/x-matroska",
        [".3gp"] = "video/3gpp",
        [".mts"] = "video/mp2t",
        [".m2ts"] = "video/mp2t",
        [".wmv"] = "video/x-ms-wmv",
        [".mpg"] = "video/mpeg",
        [".mpeg"] = "video/mpeg",
        [".mod"] = "video/mpeg",
        [".tod"] = "video/mp2t",
        [".m2t"] = "video/mp2t",
        [".vob"] = "video/mpeg",
        [".dv"] = "video/dv",
    };

    /// <summary>MIME type from the extension; generic binary for anything that is not a photo or video.</summary>
    public static string ContentType(string path) =>
        ContentTypes.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");

    [GeneratedRegex(@"^(VIDEO_TS|VTS_\d{2}_0)\.VOB$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DvdMenu();

    public static MediaKind Classify(string path)
    {
        var name = Path.GetFileName(path);

        // macOS writes a "._" companion next to every file it copies to a non-Mac drive.
        if (name.StartsWith("._", StringComparison.Ordinal))
        {
            return MediaKind.Other;
        }

        var extension = Path.GetExtension(name);
        if (PhotoExtensions.Contains(extension))
        {
            return MediaKind.Photo;
        }

        if (VideoExtensions.Contains(extension))
        {
            // A DVD's menus (VIDEO_TS.VOB, VTS_nn_0.VOB) are not footage; only title parts (VTS_nn_1.VOB on) are.
            if (DvdMenu().IsMatch(name))
            {
                return MediaKind.Other;
            }

            return MediaKind.Video;
        }

        return MediaKind.Other;
    }
}
