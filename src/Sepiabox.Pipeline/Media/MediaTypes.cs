namespace Sepiabox.Pipeline.Media;

public static class MediaTypes
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
    };

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
            return MediaKind.Video;
        }

        return MediaKind.Other;
    }
}
