using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Pipeline.Storage;

/// <summary>
/// Where each kind of file lives in Blob Storage (D-004, D-031). Shared by <c>upload</c>, which writes
/// these blobs, and the worker, which reads manifests and records derivative paths for the web.
/// </summary>
public static class BlobLayout
{
    public const string OriginalsContainer = "originals";
    public const string DerivativesContainer = "derivatives";
    public const string ManifestsContainer = "manifests";

    public static string ManifestBlobName(string assetId) => assetId + ".json";

    /// <summary><c>&lt;sha256&gt;/thumb-256.webp</c>: the work-folder file name under the asset's id.</summary>
    public static string DerivativeBlobName(string assetId, ManifestDerivative derivative) =>
        $"{assetId}/{Path.GetFileName(derivative.Path)}";
}
