using System.Globalization;
using Azure.Storage.Blobs;
using Sepiabox.Storage.Azure;
using Testcontainers.Azurite;

namespace Sepiabox.Storage.Tests;

/// <summary>
/// What every <see cref="IBlobStore"/> must do (S-050): the same tests run on the in-memory store and on Azure Blob
/// Storage as Azurite emulates it, so a test written on the one holds for the other.
/// </summary>
public abstract class BlobStoreContract
{
    protected const string Container = "contract";

    /// <summary>The store under test, with <see cref="Container"/> created.</summary>
    protected abstract IBlobStore Store { get; }

    [Fact]
    public async Task A_missing_blob_reads_as_null()
    {
        Assert.Null(await Store.ReadAsync(Container, "nothing.json"));
        Assert.Null(await Store.ReadAsync("no-such-container", "nothing.json"));
    }

    [Fact]
    public async Task Reads_back_what_was_written_with_a_version_and_a_time()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-5);

        var written = await Write("a.json", "{\"a\":1}");
        var read = await Store.ReadAsync(Container, "a.json");

        Assert.Equal(BlobWriteStatus.Written, written.Status);
        Assert.NotNull(read);
        Assert.Equal("{\"a\":1}", read.Content.ToString());
        Assert.Equal(written.Version, read.Version);
        Assert.False(string.IsNullOrEmpty(read.Version));
        Assert.InRange(read.LastModified, before, DateTimeOffset.UtcNow.AddMinutes(5));
    }

    [Fact]
    public async Task Writing_if_new_fails_when_the_blob_exists_and_leaves_it_alone()
    {
        await Write("a.json", "first");

        var lost = await Store.WriteAsync(Container, "a.json", BinaryData.FromString("second"), "text/plain", BlobWriteCondition.IfNew);

        Assert.Equal(BlobWriteStatus.ConditionNotMet, lost.Status);
        Assert.Null(lost.Version);
        Assert.Equal("first", (await Store.ReadAsync(Container, "a.json"))!.Content.ToString());
    }

    [Fact]
    public async Task Writing_if_new_creates_a_blob_that_is_not_there()
    {
        var written = await Store.WriteAsync(Container, "a.json", BinaryData.FromString("first"), "text/plain", BlobWriteCondition.IfNew);

        Assert.Equal(BlobWriteStatus.Written, written.Status);
        Assert.Equal("first", (await Store.ReadAsync(Container, "a.json"))!.Content.ToString());
    }

    [Fact]
    public async Task Writing_if_the_version_matches_replaces_the_blob_and_gives_a_new_version()
    {
        var first = await Write("a.json", "first");

        var second = await Store.WriteAsync(Container, "a.json", BinaryData.FromString("second"), "text/plain", BlobWriteCondition.IfVersion(first.Version!));

        Assert.Equal(BlobWriteStatus.Written, second.Status);
        Assert.NotEqual(first.Version, second.Version);
        var read = await Store.ReadAsync(Container, "a.json");
        Assert.Equal("second", read!.Content.ToString());
        Assert.Equal(second.Version, read.Version);
    }

    [Fact]
    public async Task Writing_if_the_version_matches_fails_after_another_write()
    {
        var first = await Write("a.json", "first");
        await Write("a.json", "somebody else's");

        var lost = await Store.WriteAsync(Container, "a.json", BinaryData.FromString("mine"), "text/plain", BlobWriteCondition.IfVersion(first.Version!));

        Assert.Equal(BlobWriteStatus.ConditionNotMet, lost.Status);
        Assert.Equal("somebody else's", (await Store.ReadAsync(Container, "a.json"))!.Content.ToString());
    }

    [Fact]
    public async Task Writing_if_the_version_matches_fails_when_the_blob_is_gone()
    {
        var first = await Write("a.json", "first");
        await Store.DeleteAsync(Container, "a.json");

        var lost = await Store.WriteAsync(Container, "a.json", BinaryData.FromString("mine"), "text/plain", BlobWriteCondition.IfVersion(first.Version!));

        Assert.Equal(BlobWriteStatus.ConditionNotMet, lost.Status);
        Assert.Null(await Store.ReadAsync(Container, "a.json"));
    }

    [Fact]
    public async Task Writing_always_overwrites()
    {
        await Write("a.json", "first");

        var second = await Store.WriteAsync(Container, "a.json", BinaryData.FromString("second"), "text/plain", BlobWriteCondition.Always);

        Assert.Equal(BlobWriteStatus.Written, second.Status);
        Assert.Equal("second", (await Store.ReadAsync(Container, "a.json"))!.Content.ToString());
    }

    [Fact]
    public async Task Two_writers_with_the_same_version_cannot_both_win()
    {
        var first = await Write("a.json", "first");

        var results = await Task.WhenAll(
            Store.WriteAsync(Container, "a.json", BinaryData.FromString("one"), "text/plain", BlobWriteCondition.IfVersion(first.Version!)),
            Store.WriteAsync(Container, "a.json", BinaryData.FromString("two"), "text/plain", BlobWriteCondition.IfVersion(first.Version!)));

        Assert.Single(results, r => r.Status == BlobWriteStatus.Written);
        Assert.Single(results, r => r.Status == BlobWriteStatus.ConditionNotMet);
    }

    [Fact]
    public async Task Lists_only_the_names_that_start_with_the_prefix_in_name_order()
    {
        foreach (var name in new[] { "b/2.json", "a/1.json", "b/1.json", "c.json" })
        {
            await Write(name, name);
        }

        var all = await Names(Store.ListAsync(Container));
        var prefixed = await Names(Store.ListAsync(Container, "b/"));
        var none = await Names(Store.ListAsync(Container, "z"));

        Assert.Equal(["a/1.json", "b/1.json", "b/2.json", "c.json"], all);
        Assert.Equal(["b/1.json", "b/2.json"], prefixed);
        Assert.Empty(none);
    }

    [Fact]
    public async Task A_listing_gives_each_blob_the_version_a_read_gives()
    {
        await Write("a.json", "first");
        await Write("b.json", "second");

        var listed = await ToListAsync(Store.ListAsync(Container));

        foreach (var blob in listed)
        {
            Assert.Equal((await Store.ReadAsync(Container, blob.Name))!.Version, blob.Version);
        }

        Assert.All(listed, blob => Assert.NotEqual(default, blob.LastModified));
    }

    [Fact]
    public async Task Lists_in_pages_that_continue_where_the_last_stopped()
    {
        foreach (var n in Enumerable.Range(1, 5))
        {
            await Write($"m{n}.json", "x");
        }

        var names = new List<string>();
        string? token = null;
        var pages = 0;
        do
        {
            var page = await Store.ListPageAsync(Container, null, token, pageSize: 2);
            names.AddRange(page.Items.Select(i => i.Name));
            token = page.ContinuationToken;
            pages++;
        }
        while (token is not null && pages < 10);

        Assert.Equal(["m1.json", "m2.json", "m3.json", "m4.json", "m5.json"], names);
        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task A_page_of_a_container_that_is_not_there_is_empty()
    {
        var page = await Store.ListPageAsync("no-such-container", null, null, pageSize: 10);

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task Deletes_a_blob_once()
    {
        await Write("a.json", "first");

        Assert.True(await Store.DeleteAsync(Container, "a.json"));
        Assert.False(await Store.DeleteAsync(Container, "a.json"));
        Assert.Null(await Store.ReadAsync(Container, "a.json"));
    }

    [Fact]
    public async Task Knows_which_containers_exist()
    {
        Assert.True(await Store.ContainerExistsAsync(Container));
        Assert.False(await Store.ContainerExistsAsync("no-such-container"));
    }

    [Fact]
    public async Task Signs_a_read_url_that_names_the_blob_and_when_it_stops_working()
    {
        await Write("a/b.json", "x");
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);

        var url = await Store.CreateReadUrlAsync(Container, "a/b.json", expires, new BlobReadUrlOptions("Praia à tarde.jpg", "image/jpeg"));

        Assert.Contains(Container + "/a/b.json", url.Host + url.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("Praia", Uri.UnescapeDataString(url.Query), StringComparison.Ordinal);
        Assert.Contains("image%2Fjpeg", url.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expires.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), Uri.UnescapeDataString(url.Query), StringComparison.Ordinal);
    }

    private Task<BlobWriteResult> Write(string name, string content) =>
        Store.WriteAsync(Container, name, BinaryData.FromString(content), "text/plain", BlobWriteCondition.Always);

    private static async Task<List<string>> Names(IAsyncEnumerable<BlobListing> listing) =>
        [.. (await ToListAsync(listing)).Select(b => b.Name)];

    private static async Task<List<BlobListing>> ToListAsync(IAsyncEnumerable<BlobListing> listing)
    {
        var all = new List<BlobListing>();
        await foreach (var blob in listing)
        {
            all.Add(blob);
        }

        return all;
    }
}

/// <summary>On <see cref="InMemoryBlobStore"/>.</summary>
public sealed class InMemoryBlobStoreTests : BlobStoreContract
{
    public InMemoryBlobStoreTests()
    {
        Memory.CreateContainer(Container);
    }

    private InMemoryBlobStore Memory { get; } = new();

    protected override IBlobStore Store => Memory;

    [Fact]
    public async Task Remembers_the_content_type_a_blob_was_written_with()
    {
        await Store.WriteAsync(Container, "a.json", BinaryData.FromString("x"), "application/json", BlobWriteCondition.Always);

        Assert.Equal("application/json", Memory.ContentTypeOf(Container, "a.json"));
        Assert.Null(Memory.ContentTypeOf(Container, "b.json"));
    }

    [Fact]
    public async Task Does_not_change_what_it_holds_when_the_caller_changes_its_bytes()
    {
        var bytes = new byte[] { 1, 2, 3 };
        await Store.WriteAsync(Container, "a.bin", BinaryData.FromBytes(bytes), "application/octet-stream", BlobWriteCondition.Always);

        bytes[0] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, (await Store.ReadAsync(Container, "a.bin"))!.Content.ToArray());
    }
}

/// <summary>On Azure Blob Storage, as Azurite emulates it.</summary>
[Trait("Category", "Integration")]
public sealed class AzureBlobStoreTests : BlobStoreContract, IAsyncLifetime, IDisposable
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        .WithInMemoryPersistence()
        // The storage SDK speaks a newer API version than Azurite announces; Aspire's emulator does the same.
        .WithCommand("--skipApiVersionCheck")
        .Build();

    private AzureBlobStore? _store;

    protected override IBlobStore Store => _store ?? throw new InvalidOperationException("Azurite is not started.");

    public async Task InitializeAsync()
    {
        await _azurite.StartAsync();
        var service = new BlobServiceClient(_azurite.GetConnectionString());
        await service.GetBlobContainerClient(Container).CreateAsync();
        _store = new AzureBlobStore(service);
    }

    public async Task DisposeAsync() => await _azurite.DisposeAsync();

    public void Dispose() => _store?.Dispose();
}
