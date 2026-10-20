namespace Sepiabox.Web.Services;

/// <summary>The asset kinds written in manifests; the UI names them through <c>UiText.KindLabel</c>.</summary>
public static class AssetKinds
{
    public const string Photo = "photo";
    public const string Video = "video";

    public static bool IsVideo(string kind) => kind == Video;
}
