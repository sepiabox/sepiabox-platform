namespace Sepiabox.Web.Services;

/// <summary>Derivatives the web serves (D-015); anything else in a URL is not a derivative.</summary>
public static class DerivativeNames
{
    public const string Thumbnail = "thumb-256";
    public const string Preview = "preview-1280";

    /// <summary>A video's still frame, shown where a photo shows its preview.</summary>
    public const string Poster = "poster";

    /// <summary>A video's 720p H.264 version, the one the browser plays (D-015).</summary>
    public const string Proxy = "proxy-720p";

    public static readonly IReadOnlyList<string> Served = [Thumbnail, Preview, Poster, Proxy];

    public static bool IsServed(string name) => Served.Contains(name, StringComparer.Ordinal);
}
