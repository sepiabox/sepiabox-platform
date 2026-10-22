using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Sepiabox.Storage;

/// <summary>
/// An <see cref="IBlobStore"/> in memory, for unit tests and local experiments, with the same conditional-write rules
/// as Azure (S-050). Containers come into being with their first write or <see cref="CreateContainer"/>. Read URLs are
/// <c>memory://</c> addresses that say what they were asked for.
/// </summary>
public sealed class InMemoryBlobStore(TimeProvider? time = null) : IBlobStore
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, SortedDictionary<string, Stored>> _containers = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private long _versions;

    /// <summary>Makes the container exist, empty, as an Azure container created beforehand does.</summary>
    public void CreateContainer(string container) => _containers.GetOrAdd(container, _ => new SortedDictionary<string, Stored>(StringComparer.Ordinal));

    public Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default) =>
        Task.FromResult(_containers.ContainsKey(container));

    public Task<BlobContent?> ReadAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(
                _containers.TryGetValue(container, out var blobs) && blobs.TryGetValue(name, out var stored)
                    ? new BlobContent(stored.Content, stored.Version, stored.LastModified)
                    : null);
        }
    }

    public async IAsyncEnumerable<BlobListing> ListAsync(
        string container, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<BlobListing> found;
        lock (_lock)
        {
            found = _containers.TryGetValue(container, out var blobs)
                ? [.. blobs.Where(b => prefix is null || b.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(b => new BlobListing(b.Key, b.Value.Version, b.Value.LastModified))]
                : [];
        }

        foreach (var blob in found)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return blob;
        }

        await Task.CompletedTask;
    }

    public Task<BlobPage> ListPageAsync(
        string container, string? prefix, string? continuationToken, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        lock (_lock)
        {
            // The token is the last name returned: the next page starts after it.
            var found = _containers.TryGetValue(container, out var blobs)
                ? blobs
                    .Where(b => (prefix is null || b.Key.StartsWith(prefix, StringComparison.Ordinal))
                        && (continuationToken is null || string.CompareOrdinal(b.Key, continuationToken) > 0))
                    .Take(pageSize + 1)
                    .Select(b => new BlobListing(b.Key, b.Value.Version, b.Value.LastModified))
                    .ToList()
                : [];
            var more = found.Count > pageSize;
            var items = found.Take(pageSize).ToList();
            return Task.FromResult(new BlobPage(items, more ? items[^1].Name : null));
        }
    }

    public Task<BlobWriteResult> WriteAsync(
        string container, string name, BinaryData content, string contentType, BlobWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        lock (_lock)
        {
            var blobs = _containers.GetOrAdd(container, _ => new SortedDictionary<string, Stored>(StringComparer.Ordinal));
            var exists = blobs.TryGetValue(name, out var current);
            var holds = condition.Kind switch
            {
                BlobWriteConditionKind.IfNew => !exists,
                BlobWriteConditionKind.IfVersion => exists && current!.Version == condition.Version,
                _ => true,
            };
            if (!holds)
            {
                return Task.FromResult(new BlobWriteResult(BlobWriteStatus.ConditionNotMet));
            }

            var version = (++_versions).ToString(CultureInfo.InvariantCulture);
            blobs[name] = new Stored(BinaryData.FromBytes(content.ToArray()), contentType, version, _time.GetUtcNow());
            return Task.FromResult(new BlobWriteResult(BlobWriteStatus.Written, version));
        }
    }

    public Task<bool> DeleteAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_containers.TryGetValue(container, out var blobs) && blobs.Remove(name));
        }
    }

    public Task<Uri> CreateReadUrlAsync(
        string container, string name, DateTimeOffset expiresOn, BlobReadUrlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"memory://{container}/{Uri.EscapeDataString(name).Replace("%2F", "/", StringComparison.Ordinal)}"
            + $"?se={Uri.EscapeDataString(expiresOn.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}"
            + (options?.DownloadFileName is { } file ? $"&filename={Uri.EscapeDataString(file)}" : string.Empty)
            + (options?.ContentType is { } type ? $"&type={Uri.EscapeDataString(type)}" : string.Empty);
        return Task.FromResult(new Uri(url));
    }

    /// <summary>The content type a blob was written with, for a test; null when it is not there.</summary>
    public string? ContentTypeOf(string container, string name)
    {
        lock (_lock)
        {
            return _containers.TryGetValue(container, out var blobs) && blobs.TryGetValue(name, out var stored) ? stored.ContentType : null;
        }
    }

    private sealed record Stored(BinaryData Content, string ContentType, string Version, DateTimeOffset LastModified);
}
