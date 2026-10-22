namespace Sepiabox.Storage;

/// <summary>
/// Where each kind of file lives in Blob Storage (D-004, D-031). Shared by <c>upload</c>, which writes
/// these blobs, the worker, which reads manifests and records derivative paths for the web, and the web, which
/// asks for links to them.
/// </summary>
public static class BlobLayout
{
    public const string OriginalsContainer = "originals";
    public const string DerivativesContainer = "derivatives";
    public const string ManifestsContainer = "manifests";

    public static string ManifestBlobName(string assetId) => assetId + ".json";

    /// <summary><c>&lt;sha256&gt;/thumb-256.webp</c>: the work-folder file name under the asset's id.</summary>
    /// <param name="assetId">The asset's id.</param>
    /// <param name="derivativePath">The derivative's path in the manifest, with <c>/</c> or <c>\</c>.</param>
    public static string DerivativeBlobName(string assetId, string derivativePath)
    {
        ArgumentNullException.ThrowIfNull(derivativePath);
        // Not Path.GetFileName: on Linux it ignores '\', so a manifest written on Windows would differ.
        var fileName = derivativePath[(derivativePath.LastIndexOfAny(['/', '\\']) + 1)..];
        return $"{assetId}/{fileName}";
    }
}
