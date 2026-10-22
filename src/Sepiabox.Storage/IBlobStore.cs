namespace Sepiabox.Storage;

/// <summary>A blob read: its bytes, its version tag and when it was last written.</summary>
/// <param name="Content">The blob's bytes.</param>
/// <param name="Version">Changes with every write; what a conditional write compares (an ETag in Azure).</param>
/// <param name="LastModified">When the blob was last written, on the store's clock.</param>
public sealed record BlobContent(BinaryData Content, string Version, DateTimeOffset LastModified);

/// <summary>A blob in a listing.</summary>
/// <param name="Name">The blob's name in its container.</param>
/// <param name="Version">Its version tag, as <see cref="IBlobStore.ReadAsync"/> would give it.</param>
/// <param name="LastModified">
/// When it was last written, on the store's clock; <see cref="DateTimeOffset.MaxValue"/> when the store does not say, so
/// that a caller that skips blobs older than some time keeps this one (a sweep that erases data must not skip a blob).
/// </param>
public sealed record BlobListing(string Name, string Version, DateTimeOffset LastModified);

/// <summary>One page of a listing: <see cref="ContinuationToken"/> asks for the next, and is null on the last.</summary>
public sealed record BlobPage(IReadOnlyList<BlobListing> Items, string? ContinuationToken);

/// <summary>What a write needs to be true of the blob already there, if anything.</summary>
public readonly record struct BlobWriteCondition
{
    private BlobWriteCondition(BlobWriteConditionKind kind, string? version)
    {
        Kind = kind;
        Version = version;
    }

    public BlobWriteConditionKind Kind { get; }

    /// <summary>The version the blob must still have, for <see cref="BlobWriteConditionKind.IfVersion"/>.</summary>
    public string? Version { get; }

    /// <summary>Overwrite whatever is there, or create the blob.</summary>
    public static BlobWriteCondition Always { get; } = new(BlobWriteConditionKind.Always, null);

    /// <summary>Only create the blob: lost if one is there.</summary>
    public static BlobWriteCondition IfNew { get; } = new(BlobWriteConditionKind.IfNew, null);

    /// <summary>Only replace the blob if it still has <paramref name="version"/>: lost if it changed or is gone.</summary>
    public static BlobWriteCondition IfVersion(string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        return new BlobWriteCondition(BlobWriteConditionKind.IfVersion, version);
    }
}

public enum BlobWriteConditionKind
{
    Always,
    IfNew,
    IfVersion,
}

public enum BlobWriteStatus
{
    Written,

    /// <summary>Somebody else wrote the blob first (or it was never there): not an error, read it and decide again.</summary>
    ConditionNotMet,
}

/// <param name="Status">Whether the blob was written.</param>
/// <param name="Version">The new version, when written.</param>
public sealed record BlobWriteResult(BlobWriteStatus Status, string? Version = null);

/// <summary>How a signed read URL presents the blob.</summary>
/// <param name="DownloadFileName">Download the blob under this name instead of showing it.</param>
/// <param name="ContentType">Answer with this content type instead of the one stored.</param>
public sealed record BlobReadUrlOptions(string? DownloadFileName = null, string? ContentType = null);

/// <summary>
/// The blobs the project reads and writes (S-050, D-051): an Azure Blob Storage implementation, and an in-memory one for
/// tests. It has only what a caller needs today; S-051 adds the staged upload and the access tier.
/// </summary>
public interface IBlobStore
{
    /// <summary>Whether the container exists.</summary>
    Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>The blob, or null when it is not there (nor its container).</summary>
    Task<BlobContent?> ReadAsync(string container, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="exception"/> is the store failing a request (a refused call, a timeout) rather than a bug in
    /// the caller, so that a caller with many things to do can report this one and go on. Not a missing blob or container.
    /// </summary>
    bool IsStoreFailure(Exception exception) => false;

    /// <summary>
    /// The blobs of the container whose name starts with <paramref name="prefix"/> (all when null), in name order. A
    /// container that is not there has none, as for <see cref="ListPageAsync"/>.
    /// </summary>
    IAsyncEnumerable<BlobListing> ListAsync(string container, string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the listing, for a caller that saves where it got to (the faces sweep, S-022): start with a null
    /// <paramref name="continuationToken"/>, then give the one the page returned. A container that is not there is an empty page.
    /// </summary>
    Task<BlobPage> ListPageAsync(
        string container, string? prefix, string? continuationToken, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the blob if <paramref name="condition"/> holds. A condition that does not hold is
    /// <see cref="BlobWriteStatus.ConditionNotMet"/>, never an exception; any other failure is one, and so is a container
    /// that is not there (a write does not create it: the container is set up beforehand).
    /// </summary>
    Task<BlobWriteResult> WriteAsync(
        string container, string name, BinaryData content, string contentType, BlobWriteCondition condition,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the blob; false when it was not there.</summary>
    Task<bool> DeleteAsync(string container, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// A read-only link to one blob that stops working at <paramref name="expiresOn"/>, for a browser: the store signs it,
    /// so the caller never holds storage credentials (D-004).
    /// </summary>
    Task<Uri> CreateReadUrlAsync(
        string container, string name, DateTimeOffset expiresOn, BlobReadUrlOptions? options = null,
        CancellationToken cancellationToken = default);
}
