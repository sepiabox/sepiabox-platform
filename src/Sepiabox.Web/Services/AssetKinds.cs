namespace Sepiabox.Web.Services;

/// <summary>The asset kinds written in manifests, and how the UI names them.</summary>
public static class AssetKinds
{
    public const string Photo = "photo";
    public const string Video = "video";

    public static bool IsVideo(string kind) => kind == Video;

    public static string Label(string kind) => IsVideo(kind) ? "Vídeo" : "Foto";
}
