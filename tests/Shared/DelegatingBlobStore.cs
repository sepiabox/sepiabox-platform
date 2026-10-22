using System.Diagnostics.CodeAnalysis;
using Sepiabox.Storage;

namespace Sepiabox.Tests.Shared;

/// <summary>An <see cref="IBlobStore"/> that passes every call to another, for a test to change one of them (S-050).</summary>
[SuppressMessage("Performance", "CA1852", Justification = "Subclassed by some of the test projects that compile this file.")]
internal class DelegatingBlobStore(IBlobStore inner) : IBlobStore
{
    protected IBlobStore Inner { get; } = inner;

    public virtual Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default) =>
        Inner.ContainerExistsAsync(container, cancellationToken);

    public virtual Task<BlobContent?> ReadAsync(string container, string name, CancellationToken cancellationToken = default) =>
        Inner.ReadAsync(container, name, cancellationToken);

    public virtual IAsyncEnumerable<BlobListing> ListAsync(string container, string? prefix = null, CancellationToken cancellationToken = default) =>
        Inner.ListAsync(container, prefix, cancellationToken);

    public virtual Task<BlobPage> ListPageAsync(
        string container, string? prefix, string? continuationToken, int pageSize, CancellationToken cancellationToken = default) =>
        Inner.ListPageAsync(container, prefix, continuationToken, pageSize, cancellationToken);

    public virtual Task<BlobWriteResult> WriteAsync(
        string container, string name, BinaryData content, string contentType, BlobWriteCondition condition,
        CancellationToken cancellationToken = default) =>
        Inner.WriteAsync(container, name, content, contentType, condition, cancellationToken);

    public virtual Task<BlobWriteResult> WriteAsync(
        string container, string name, BinaryData content, string contentType, BlobWriteCondition condition,
        IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default) =>
        Inner.WriteAsync(container, name, content, contentType, condition, metadata, cancellationToken);

    public virtual Task<BlobInfo?> DownloadToAsync(string container, string name, Stream stream, CancellationToken cancellationToken = default) =>
        Inner.DownloadToAsync(container, name, stream, cancellationToken);

    public virtual Task CopyAsync(
        string sourceContainer, string sourceName, string container, string name, bool cold = false, CancellationToken cancellationToken = default) =>
        Inner.CopyAsync(sourceContainer, sourceName, container, name, cold, cancellationToken);

    public virtual Task<bool> DeleteAsync(string container, string name, CancellationToken cancellationToken = default) =>
        Inner.DeleteAsync(container, name, cancellationToken);

    public virtual Task<Uri> CreateReadUrlAsync(
        string container, string name, DateTimeOffset expiresOn, BlobReadUrlOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Inner.CreateReadUrlAsync(container, name, expiresOn, options, cancellationToken);
}
