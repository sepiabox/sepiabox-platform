namespace Sepiabox.Storage;

/// <summary>A blob read: its bytes, its version tag and when it was last written.</summary>
/// <param name="Content">The blob's bytes.</param>
/// <param name="Version">Changes with every write; what a conditional write compares (an ETag in Azure).</param>
/// <param name="LastModified">When the blob was last written, on the store's clock.</param>
public sealed record BlobContent(BinaryData Content, string Version, DateTimeOffset LastModified)
{
    /// <summary>The blob's metadata (S-036): name/value pairs written with it; empty when it has none.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>What a blob is, without its bytes (S-036): its size, version and metadata, as <see cref="IBlobStore.DownloadToAsync"/> reports it.</summary>
/// <param name="Length">The size in bytes.</param>
/// <param name="Version">Its version tag, as <see cref="BlobContent.Version"/>.</param>
/// <param name="LastModified">When it was last written, on the store's clock.</param>
/// <param name="ContentType">The content type it was written with.</param>
/// <param name="Metadata">The metadata it was written with; empty when none.</param>
public sealed record BlobInfo(long Length, string Version, DateTimeOffset LastModified, string ContentType, IReadOnlyDictionary<string, string> Metadata);

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

/// <summary>What a conditional delete came to (S-057).</summary>
public enum BlobDeleteStatus
{
    Deleted,

    /// <summary>The blob was not there: already gone, which a deletion counts as done.</summary>
    NotFound,

    /// <summary>The blob changed since it was read (someone wrote it meanwhile): not deleted, read it and decide again.</summary>
    ConditionNotMet,
}

/// <summary>How a signed read URL presents the blob.</summary>
/// <param name="DownloadFileName">Download the blob under this name instead of showing it.</param>
/// <param name="ContentType">Answer with this content type instead of the one stored.</param>
public sealed record BlobReadUrlOptions(string? DownloadFileName = null, string? ContentType = null);

/// <summary>How a blob is kept, and so what it costs to keep and to read (D-004).</summary>
public enum BlobStorageClass
{
    /// <summary>The store's own default: Hot on Azure.</summary>
    Default,

    /// <summary>Cheap to keep and dear to read: the originals, which are read rarely. A store that has no such class (Azurite, memory) keeps the blob as usual.</summary>
    Cold,
}

/// <summary>
/// A blob being written block by block (S-051, D-030): the caller reads its source once, hashes it as it goes and
/// commits only if the hash is the one it expects. Blocks go in the order they are appended; nothing is visible in the
/// container until <see cref="CommitAsync(string, BlobStorageClass, CancellationToken)"/>, and an upload that is disposed without being committed leaves no blob.
/// </summary>
public interface IStagedUpload : IAsyncDisposable
{
    /// <summary>Adds the next block (the bytes are copied or sent before this returns, so the caller may reuse them).</summary>
    Task AppendAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken = default);

    /// <summary>Makes the blob from the blocks appended so far, replacing one that is there. An upload commits once: a second commit, or an append after it, throws <see cref="InvalidOperationException"/>.</summary>
    Task CommitAsync(string contentType, BlobStorageClass storageClass = BlobStorageClass.Default, CancellationToken cancellationToken = default);

    /// <summary>
    /// As the commit above, and the blob has <paramref name="metadata"/> from the moment it exists (S-036: a connector that
    /// streams a large file into <c>incoming</c> gives its source, path and mode in the same call that makes the blob, so
    /// no event ever finds the blob without them). Names and values follow the rules of <see cref="IBlobStore.WriteAsync(string, string, BinaryData, string, BlobWriteCondition, IReadOnlyDictionary{string, string}, CancellationToken)"/>.
    /// </summary>
    Task CommitAsync(
        string contentType, BlobStorageClass storageClass, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This store does not commit metadata.");
}

/// <summary>
/// The blobs the project reads and writes (S-050, S-051, D-051): an Azure Blob Storage implementation, and an in-memory
/// one for tests. It has only what a caller needs today.
/// </summary>
public interface IBlobStore
{
    /// <summary>Whether the container exists.</summary>
    Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Creates the container unless it is there.</summary>
    Task CreateContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>The size in bytes of the blob, or null when it is not there (nor its container).</summary>
    Task<long?> GetSizeAsync(string container, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// The time on the store's own clock, as it answers a request about the container (the <c>Date</c> header in Azure,
    /// in whole seconds), for <c>upload</c> to compare with the computer's (D-081); null when the store does not say.
    /// The container must exist.
    /// </summary>
    Task<DateTimeOffset?> GetServerTimeAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Starts writing a blob in blocks. Nothing reaches the store until the first block, and no blob until the commit.</summary>
    IStagedUpload BeginUpload(string container, string name);

    /// <summary>The blob, or null when it is not there (nor its container).</summary>
    Task<BlobContent?> ReadAsync(string container, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="exception"/> is the store failing a request (a refused call, a timeout) rather than a bug in
    /// the caller, so that a caller with many things to do can report this one and go on. Not a missing blob or container.
    /// A store that wraps another (a decorator, a test double) must forward this to it: the default says no, and a failure
    /// that is not recognised stops the caller as a bug.
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

    /// <summary>
    /// Writes the blob with <paramref name="metadata"/> (S-036: the cloud source, path and mode of a file that arrives in
    /// <c>incoming</c>). Metadata names are letters and digits, starting with a letter, and values are ASCII without
    /// control characters: a caller encodes anything else.
    /// </summary>
    Task<BlobWriteResult> WriteAsync(
        string container, string name, BinaryData content, string contentType, BlobWriteCondition condition,
        IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This store does not write metadata.");

    /// <summary>
    /// Copies the blob into <paramref name="stream"/> without holding it in memory (a video can be gigabytes), and says what
    /// it is; null, with the stream untouched, when it is not there (S-036).
    /// </summary>
    Task<BlobInfo?> DownloadToAsync(string container, string name, Stream stream, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This store does not stream downloads.");

    /// <summary>Deletes the blob; false when it was not there.</summary>
    Task<bool> DeleteAsync(string container, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the blob only if it still has <paramref name="version"/> (S-057): a blob someone wrote since it was read is
    /// <see cref="BlobDeleteStatus.ConditionNotMet"/>, never an exception; one that is not there is
    /// <see cref="BlobDeleteStatus.NotFound"/>. Any other failure is an exception.
    /// </summary>
    Task<BlobDeleteStatus> DeleteIfVersionAsync(string container, string name, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// A read-only link to one blob that stops working at <paramref name="expiresOn"/>, for a browser: the store signs it,
    /// so the caller never holds storage credentials (D-004).
    /// </summary>
    Task<Uri> CreateReadUrlAsync(
        string container, string name, DateTimeOffset expiresOn, BlobReadUrlOptions? options = null,
        CancellationToken cancellationToken = default);
}
