namespace Sepiabox.Web.Services;

/// <summary>
/// Containers only the web's host wiring uses. The ones <c>upload</c> writes are in <c>BlobLayout</c>
/// (Sepiabox.Storage, S-050), which the web references without the media pipeline (D-038).
/// </summary>
public static class StorageContainers
{
    /// <summary>The web's data protection key ring in Azure (S-058); created by the deploy, not by upload.</summary>
    public const string DataProtection = Sepiabox.Storage.BlobLayout.DataProtectionContainer;
}
