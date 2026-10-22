using Sepiabox.Storage;

namespace Sepiabox.Web.Services;

/// <summary>
/// Short-lived read-only links to blobs, so the browser never gets storage credentials (D-004). The web
/// signs them when the browser asks (see <c>MediaEndpoints</c>), never into a page that may sit open.
/// </summary>
public interface ISasUrlFactory
{
    /// <summary>A thumbnail, preview or poster in the <c>derivatives</c> container.</summary>
    Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// A video's 720p version in the <c>derivatives</c> container, with a link that outlasts playback: the
    /// browser keeps fetching ranges of it for as long as the video plays or sits paused.
    /// </summary>
    Task<Uri> ForVideoAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>The original in the <c>originals</c> container, downloaded under its source file name.</summary>
    Task<Uri> ForOriginalAsync(string assetId, OriginalFile file, CancellationToken cancellationToken = default);
}

/// <summary>
/// Chooses how long each kind of link lasts (D-039, D-049) and asks the blob store to sign it; the store signs with the
/// account key when it has one (Azurite) and with a user delegation key otherwise (Azure, managed identity), which
/// never exposes an account key to the web.
/// </summary>
public sealed class SasUrlFactory(IBlobStore store, TimeProvider time) : ISasUrlFactory
{
    /// <summary>Derivative links expire at the end of the 5-minute window they were signed in plus this.</summary>
    public static readonly TimeSpan DerivativeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Derivative expiry is rounded down to this window, so every request in it gets the same URL and the
    /// browser cache can reuse the image. A link is therefore valid for 10 to 15 minutes.
    /// </summary>
    public static readonly TimeSpan DerivativeWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a browser may cache a redirect to a derivative or video: a minute less than the shortest
    /// link's life, so a cached redirect never points at a link that has just expired (clock skew, slow loads).
    /// </summary>
    public static readonly TimeSpan RedirectCacheDuration = DerivativeLifetime - DerivativeWindow - TimeSpan.FromMinutes(1);

    /// <summary>
    /// Video links expire at the start of the 5-minute window they were signed in plus this, so 3 h 55 to
    /// 4 h after signing: a long camcorder tape, or one paused for a while, still plays to the end (D-049).
    /// </summary>
    public static readonly TimeSpan VideoLifetime = TimeSpan.FromHours(4);

    public static readonly TimeSpan OriginalLifetime = TimeSpan.FromMinutes(5);

    public Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default) =>
        store.CreateReadUrlAsync(BlobLayout.DerivativesContainer, blobPath, SigningWindowStart() + DerivativeLifetime, null, cancellationToken);

    public Task<Uri> ForVideoAsync(string blobPath, CancellationToken cancellationToken = default) =>
        store.CreateReadUrlAsync(BlobLayout.DerivativesContainer, blobPath, SigningWindowStart() + VideoLifetime, null, cancellationToken);

    // Originals are stored under their hash, so the name and type come from the catalog.
    public Task<Uri> ForOriginalAsync(string assetId, OriginalFile file, CancellationToken cancellationToken = default) =>
        store.CreateReadUrlAsync(
            BlobLayout.OriginalsContainer, assetId, time.GetUtcNow() + OriginalLifetime, new BlobReadUrlOptions(file.FileName, file.ContentType), cancellationToken);

    // Links signed in the same window are identical, so the browser cache can reuse them.
    private DateTimeOffset SigningWindowStart()
    {
        var now = time.GetUtcNow();
        return new DateTimeOffset(now.UtcTicks - (now.UtcTicks % DerivativeWindow.Ticks), TimeSpan.Zero);
    }
}
