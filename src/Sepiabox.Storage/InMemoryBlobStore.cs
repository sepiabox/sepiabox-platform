using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Sepiabox.Storage;

/// <summary>
/// An <see cref="IBlobStore"/> in memory, for unit tests and local experiments, with the same conditional-write rules
/// as Azure (S-050). Containers come into being with <see cref="CreateContainer"/>, as Azure containers are created beforehand; a write to one that is not there throws. Read URLs are
/// <c>memory://</c> addresses that say what they were asked for.
/// </summary>
public sealed class InMemoryBlobStore(TimeProvider? time = null) : IBlobStore
{
    /// <summary>The longest a link may last, as in Azure (<c>AzureBlobStore.MaxReadUrlLifetime</c>).</summary>
    public static readonly TimeSpan MaxReadUrlLifetime = TimeSpan.FromHours(4);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, SortedDictionary<string, Stored>> _containers = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private long _versions;

    /// <summary>Makes the container exist, empty, as an Azure container created beforehand does.</summary>
    public void CreateContainer(string container) => _containers.GetOrAdd(container, _ => new SortedDictionary<string, Stored>(StringComparer.Ordinal));

    public Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default) =>
        Task.FromResult(_containers.ContainsKey(container));

    public Task CreateContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        CreateContainer(container);
        return Task.CompletedTask;
    }

    public Task<long?> GetSizeAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<long?>(
                _containers.TryGetValue(container, out var blobs) && blobs.TryGetValue(name, out var stored) ? stored.Content.ToMemory().Length : null);
        }
    }

    public Task<DateTimeOffset?> GetServerTimeAsync(string container, CancellationToken cancellationToken = default) =>
        Task.FromResult<DateTimeOffset?>(_time.GetUtcNow());

    public IStagedUpload BeginUpload(string container, string name) => new StagedUpload(this, container, name);

    public Task<BlobContent?> ReadAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(
                _containers.TryGetValue(container, out var blobs) && blobs.TryGetValue(name, out var stored)
                    ? new BlobContent(stored.Content, stored.Version, stored.LastModified) { Metadata = stored.Metadata }
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
        CancellationToken cancellationToken = default) =>
        WriteAsync(container, name, content, contentType, condition, new Dictionary<string, string>(), cancellationToken);

    public Task<BlobWriteResult> WriteAsync(
        string container, string name, BinaryData content, string contentType, BlobWriteCondition condition,
        IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(metadata);
        lock (_lock)
        {
            if (!_containers.TryGetValue(container, out var blobs))
            {
                throw new InvalidOperationException($"The container {container} does not exist.");
            }

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
            blobs[name] = new Stored(BinaryData.FromBytes(content.ToArray()), contentType, version, _time.GetUtcNow())
            {
                Metadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase),
            };
            return Task.FromResult(new BlobWriteResult(BlobWriteStatus.Written, version));
        }
    }

    public async Task<BlobInfo?> DownloadToAsync(string container, string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Stored? stored;
        lock (_lock)
        {
            stored = _containers.TryGetValue(container, out var blobs) && blobs.TryGetValue(name, out var found) ? found : null;
        }

        if (stored is null)
        {
            return null;
        }

        await stream.WriteAsync(stored.Content.ToMemory(), cancellationToken);
        return new BlobInfo(stored.Content.ToMemory().Length, stored.Version, stored.LastModified, stored.ContentType, stored.Metadata);
    }

    public Task<bool> DeleteAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_containers.TryGetValue(container, out var blobs) && blobs.Remove(name));
        }
    }

    public Task<BlobDeleteStatus> DeleteIfVersionAsync(string container, string name, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        lock (_lock)
        {
            if (!_containers.TryGetValue(container, out var blobs) || !blobs.TryGetValue(name, out var stored))
            {
                return Task.FromResult(BlobDeleteStatus.NotFound);
            }

            if (stored.Version != version)
            {
                return Task.FromResult(BlobDeleteStatus.ConditionNotMet);
            }

            blobs.Remove(name);
            return Task.FromResult(BlobDeleteStatus.Deleted);
        }
    }

    public Task<Uri> CreateReadUrlAsync(
        string container, string name, DateTimeOffset expiresOn, BlobReadUrlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (expiresOn > _time.GetUtcNow() + MaxReadUrlLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresOn), expiresOn, $"A link lasts at most {MaxReadUrlLifetime}.");
        }

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

    /// <summary>The storage class a blob was committed with by a staged upload, for a test; null when it is not there.</summary>
    public BlobStorageClass? StorageClassOf(string container, string name)
    {
        lock (_lock)
        {
            return _containers.TryGetValue(container, out var blobs) && blobs.TryGetValue(name, out var stored) ? stored.StorageClass : null;
        }
    }

    // The blocks are kept in the upload, apart from the container, until the commit.
    private sealed class StagedUpload(InMemoryBlobStore store, string container, string name) : IStagedUpload
    {
        private readonly List<byte[]> _blocks = [];
        private bool _committed;

        public Task AppendAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken = default)
        {
            if (_committed)
            {
                throw new InvalidOperationException("The upload is already committed.");
            }

            _blocks.Add(block.ToArray());
            return Task.CompletedTask;
        }

        public Task CommitAsync(string contentType, BlobStorageClass storageClass = BlobStorageClass.Default, CancellationToken cancellationToken = default) =>
            CommitAsync(contentType, storageClass, new Dictionary<string, string>(), cancellationToken);

        public Task CommitAsync(
            string contentType, BlobStorageClass storageClass, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            if (_committed)
            {
                throw new InvalidOperationException("The upload is already committed.");
            }

            var content = new byte[_blocks.Sum(b => b.Length)];
            var offset = 0;
            foreach (var block in _blocks)
            {
                block.CopyTo(content, offset);
                offset += block.Length;
            }

            lock (store._lock)
            {
                if (!store._containers.TryGetValue(container, out var blobs))
                {
                    throw new InvalidOperationException($"The container {container} does not exist.");
                }

                blobs[name] = new Stored(BinaryData.FromBytes(content), contentType, (++store._versions).ToString(CultureInfo.InvariantCulture), store._time.GetUtcNow(), storageClass)
                {
                    Metadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase),
                };
            }

            _committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record Stored(
        BinaryData Content, string ContentType, string Version, DateTimeOffset LastModified, BlobStorageClass StorageClass = BlobStorageClass.Default)
    {
        public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    }
}
