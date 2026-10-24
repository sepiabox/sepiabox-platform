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

    /// <summary>Files that arrive from a cloud source without a manifest, until the worker has processed them (S-036).</summary>
    public const string IncomingContainer = "incoming";

    /// <summary>
    /// The container, blob and application name of the web's data protection key ring (S-058, D-062). The sync job (S-038) reads the
    /// same ring to unprotect the refresh tokens the web protected, so both name these.
    /// </summary>
    public const string DataProtectionContainer = "dataprotection";

    public const string KeyRingBlobName = "keys.xml";

    public const string KeyRingApplicationName = "sepiabox-web";

    /// <summary>
    /// The container, blob and application name of the key ring that protects the cloud accounts' refresh tokens (S-037, S-038, D-115): a
    /// ring of its own, with its own Key Vault key, written by the web and read by the sync job. The sign-in cookies' ring is the web's alone,
    /// so code running as the sync job cannot mint a session.
    /// </summary>
    public const string TokenRingContainer = "tokenring";

    public const string TokenRingBlobName = "keys.xml";

    public const string TokenRingApplicationName = "sepiabox-tokens";

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
