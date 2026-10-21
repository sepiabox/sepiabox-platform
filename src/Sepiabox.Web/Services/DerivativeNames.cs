namespace Sepiabox.Web.Services;

/// <summary>Derivatives the web serves (D-015); anything else in a URL is not a derivative.</summary>
public static class DerivativeNames
{
    public const string Thumbnail = "thumb-256";
    public const string Preview = "preview-1280";

    /// <summary>The photo preview of an index-only work folder (S-063), which stands in for the original on screen.</summary>
    public const string LargePreview = "preview-2048";

    /// <summary>A video's still frame, shown where a photo shows its preview.</summary>
    public const string Poster = "poster";

    /// <summary>A video's 720p H.264 version, the one the browser plays (D-015).</summary>
    public const string Proxy = "proxy-720p";

    /// <summary>A video's 1080p H.264 version, made instead of <see cref="Proxy"/> in an index-only work folder (S-063).</summary>
    public const string LargeProxy = "proxy-1080p";

    public static readonly IReadOnlyList<string> Served = [Thumbnail, Preview, LargePreview, Poster, Proxy, LargeProxy];

    public static bool IsServed(string name) => Served.Contains(name, StringComparer.Ordinal);

    /// <summary>Whether the derivative is a video the browser plays, which gets the video link rules (D-031).</summary>
    public static bool IsVideo(string name) => name is Proxy or LargeProxy;
}
