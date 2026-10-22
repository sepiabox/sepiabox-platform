using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Uploading;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Storage;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// <c>upload</c> on the in-memory store (S-051): what it asks of <see cref="IBlobStore"/>, without Docker. The same
/// behaviour on Azure Blob Storage is in <see cref="UploadCommandTests"/>.
/// </summary>
public sealed class UploadToStoreTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();
    private readonly InMemoryBlobStore _store = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public async Task Commits_nothing_and_reports_the_file_when_it_changed_after_process()
    {
        var path = _drive.Write("a.jpg", TestPhotos.Porto("red"));
        _drive.Write("b.jpg", TestPhotos.Porto("green"));
        var changedId = Process().Single(manifest => manifest.Sources[0].Path == "a.jpg").Id;
        var content = File.ReadAllBytes(path);
        content[^3] ^= 0xFF; // same size, different bytes
        File.WriteAllBytes(path, content);

        var result = await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Local), _store);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(path, problem.Path);
        Assert.Null(await _store.ReadAsync(BlobLayout.OriginalsContainer, changedId));
        Assert.Null(await _store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(changedId)));
    }

    [Fact]
    public async Task Asks_for_the_cold_class_for_originals_only_when_the_destination_is_azure()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var id = Process().Single().Id;

        await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Azure, "account"), _store);

        Assert.Equal(BlobStorageClass.Cold, _store.StorageClassOf(BlobLayout.OriginalsContainer, id));
        Assert.All(
            await ToListAsync(_store.ListAsync(BlobLayout.DerivativesContainer)),
            derivative => Assert.Equal(BlobStorageClass.Default, _store.StorageClassOf(BlobLayout.DerivativesContainer, derivative.Name)));
    }

    [Fact]
    public async Task Keeps_the_default_class_for_a_local_destination_and_the_original_content_type()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var manifest = Process().Single();

        await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Local), _store);

        Assert.Equal(BlobStorageClass.Default, _store.StorageClassOf(BlobLayout.OriginalsContainer, manifest.Id));
        Assert.Equal(manifest.ContentType, _store.ContentTypeOf(BlobLayout.OriginalsContainer, manifest.Id));
        Assert.Equal(manifest.SizeBytes, await _store.GetSizeAsync(BlobLayout.OriginalsContainer, manifest.Id));
    }

    [Fact]
    public async Task Sends_a_derivative_of_one_block_or_less_as_one_write_and_stages_only_the_original()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var manifest = Process().Single();
        var counting = new CountingStore(_store);

        var result = await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Local), counting);

        Assert.Empty(result.Problems);
        Assert.Equal([BlobLayout.OriginalsContainer + "/" + manifest.Id], counting.Staged);
        Assert.Contains(counting.Writes, w => w.StartsWith(BlobLayout.DerivativesContainer + "/", StringComparison.Ordinal));
        Assert.Equal(manifest.Derivatives.Count, (await ToListAsync(_store.ListAsync(BlobLayout.DerivativesContainer))).Count);
    }

    [Fact]
    public async Task Refuses_a_computer_clock_that_is_far_from_the_stores()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        Process();

        var failure = await Assert.ThrowsAsync<ClockSkewException>(() => UploadCommand.RunAsync(
            new UploadOptions(_work.Path, UploadDestination.Local), _store, null, new ManifestValidator(), CancellationToken.None, new Ahead(TimeSpan.FromMinutes(20))));

        Assert.True(failure.Skew > TimeSpan.FromMinutes(19));
        Assert.Null(await _store.ReadAsync(BlobLayout.OriginalsContainer, "anything"));
    }

    // An original already in storage is skipped only if it has the size the manifest says (D-030): a smaller one (a
    // transfer that stopped) or a larger one (another file under that name) is uploaded again, and replaced.
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(-1_000_000)]
    public async Task Uploads_an_original_again_when_the_one_in_storage_has_another_size(int difference)
    {
        var path = _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var manifest = Process().Single();
        var wrong = new byte[Math.Max(0, (int)manifest.SizeBytes + difference)];
        _store.CreateContainer(BlobLayout.OriginalsContainer);
        await _store.WriteAsync(BlobLayout.OriginalsContainer, manifest.Id, BinaryData.FromBytes(wrong), "image/jpeg", BlobWriteCondition.Always);

        var result = await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Local), _store);

        Assert.Empty(result.Problems);
        Assert.Equal(await File.ReadAllBytesAsync(path), (await _store.ReadAsync(BlobLayout.OriginalsContainer, manifest.Id))!.Content.ToArray());
    }

    [Fact]
    public async Task Skips_an_original_in_storage_that_has_the_size_the_manifest_says()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var manifest = Process().Single();
        _store.CreateContainer(BlobLayout.OriginalsContainer);
        var placeholder = new byte[manifest.SizeBytes];
        await _store.WriteAsync(BlobLayout.OriginalsContainer, manifest.Id, BinaryData.FromBytes(placeholder), "image/jpeg", BlobWriteCondition.Always);
        var counting = new CountingStore(_store);

        await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Local), counting);

        Assert.DoesNotContain(counting.Staged, s => s.StartsWith(BlobLayout.OriginalsContainer + "/", StringComparison.Ordinal));
        Assert.Equal(placeholder, (await _store.ReadAsync(BlobLayout.OriginalsContainer, manifest.Id))!.Content.ToArray());
    }

    // Less than one block is one write; a block or more is staged and committed with the default class, never Cold, even for an Azure
    // destination, which asks for Cold only for originals (D-100).
    [Theory]
    [InlineData(UploadCommand.BlockSize - 1, false)]
    [InlineData(UploadCommand.BlockSize, true)]
    [InlineData(UploadCommand.BlockSize + 1, true)]
    public async Task Sends_a_derivative_in_one_write_below_a_block_and_stages_it_from_a_block_up(int size, bool staged)
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var manifest = Process().Single();
        var derivative = manifest.Derivatives[0];
        var file = Path.Combine(_work.Path, derivative.Path);
        await File.WriteAllBytesAsync(file, new byte[size]);
        var counting = new CountingStore(_store);

        var result = await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Azure, "account"), counting);

        Assert.Empty(result.Problems);
        var blob = $"{BlobLayout.DerivativesContainer}/{BlobLayout.DerivativeBlobName(manifest.Id, derivative.Path)}";
        Assert.Equal(staged, counting.Staged.Contains(blob));
        Assert.Equal(!staged, counting.Writes.Contains(blob));
        Assert.Equal(size, await _store.GetSizeAsync(BlobLayout.DerivativesContainer, BlobLayout.DerivativeBlobName(manifest.Id, derivative.Path)));
        if (staged)
        {
            Assert.Equal(BlobStorageClass.Default, counting.CommittedClass[blob]);
        }

        // The original, in the same run, is the one that asked for Cold.
        Assert.Equal(BlobStorageClass.Cold, counting.CommittedClass[$"{BlobLayout.OriginalsContainer}/{manifest.Id}"]);
    }

    [Fact]
    public void Has_a_block_of_8_mib()
    {
        Assert.Equal(8 * 1024 * 1024, UploadCommand.BlockSize);
    }

    private List<Manifest> Process()
    {
        // No FFmpeg in these tests: photos get thumbnails, videos stay pending with no derivatives.
        ProcessCommand.Run(new ProcessOptions(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None));
        return [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json")
            .Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];
    }

    private static async Task<List<BlobListing>> ToListAsync(IAsyncEnumerable<BlobListing> listing)
    {
        var all = new List<BlobListing>();
        await foreach (var blob in listing)
        {
            all.Add(blob);
        }

        return all;
    }

    private sealed class CountingStore(IBlobStore inner) : DelegatingBlobStore(inner)
    {
        public List<string> Staged { get; } = [];

        public List<string> Writes { get; } = [];

        /// <summary>The class each staged upload was committed with, by "container/name".</summary>
        public Dictionary<string, BlobStorageClass> CommittedClass { get; } = [];

        public override IStagedUpload BeginUpload(string container, string name)
        {
            lock (Staged)
            {
                Staged.Add($"{container}/{name}");
            }

            return new RecordingUpload(base.BeginUpload(container, name), $"{container}/{name}", CommittedClass);
        }

        public override Task<BlobWriteResult> WriteAsync(
            string container, string name, BinaryData content, string contentType, BlobWriteCondition condition, CancellationToken cancellationToken = default)
        {
            lock (Writes)
            {
                Writes.Add($"{container}/{name}");
            }

            return base.WriteAsync(container, name, content, contentType, condition, cancellationToken);
        }
    }

    private sealed class RecordingUpload(IStagedUpload inner, string key, Dictionary<string, BlobStorageClass> committed) : IStagedUpload
    {
        public Task AppendAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken = default) => inner.AppendAsync(block, cancellationToken);

        public async Task CommitAsync(string contentType, BlobStorageClass storageClass = BlobStorageClass.Default, CancellationToken cancellationToken = default)
        {
            await inner.CommitAsync(contentType, storageClass, cancellationToken);
            lock (committed)
            {
                committed[key] = storageClass;
            }
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class Ahead(TimeSpan by) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + by;
    }
}
