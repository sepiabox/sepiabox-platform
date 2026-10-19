namespace Sepiabox.Web.Services;

/// <summary>
/// Container names written by <c>upload</c> (<c>BlobLayout</c> in Sepiabox.Pipeline). Repeated here so the
/// web does not depend on the media pipeline and its native libraries (D-038); a test keeps them equal.
/// </summary>
public static class StorageContainers
{
    public const string Originals = "originals";
    public const string Derivatives = "derivatives";
}
