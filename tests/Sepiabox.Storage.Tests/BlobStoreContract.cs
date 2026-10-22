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
    public async Task A_staged_upload_makes_the_blob_from_its_blocks_only_when_committed()
    {
        await using var upload = Store.BeginUpload(Container, "big.bin");
        await upload.AppendAsync(new byte[] { 1, 2, 3 });
        await upload.AppendAsync(new byte[] { 4, 5 });

        Assert.Null(await Store.GetSizeAsync(Container, "big.bin"));
        Assert.Null(await Store.ReadAsync(Container, "big.bin"));

        await upload.CommitAsync("application/octet-stream");

        var read = await Store.ReadAsync(Container, "big.bin");
        Assert.NotNull(read);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, read.Content.ToArray());
        Assert.Equal(5, await Store.GetSizeAsync(Container, "big.bin"));
    }

    [Fact]
    public async Task A_staged_upload_that_is_not_committed_leaves_no_blob()
    {
        await using (var upload = Store.BeginUpload(Container, "never.bin"))
        {
            await upload.AppendAsync(new byte[] { 1, 2, 3 });
        }

        Assert.Null(await Store.ReadAsync(Container, "never.bin"));
        Assert.Empty(await ToListAsync(Store.ListAsync(Container, "never")));
    }

    [Fact]
    public async Task A_staged_upload_replaces_a_blob_that_is_there_and_may_be_empty()
    {
        await Write("a.bin", "old content");
        await using var upload = Store.BeginUpload(Container, "a.bin");

        await upload.CommitAsync("application/octet-stream", BlobStorageClass.Default);

        Assert.Equal(0, await Store.GetSizeAsync(Container, "a.bin"));
    }

    [Fact]
    public async Task A_staged_upload_commits_once()
    {
        await using var upload = Store.BeginUpload(Container, "once.bin");
        await upload.AppendAsync(new byte[] { 1 });
        await upload.CommitAsync("application/octet-stream");

        await Assert.ThrowsAsync<InvalidOperationException>(() => upload.CommitAsync("application/octet-stream"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => upload.AppendAsync(new byte[] { 2 }));
    }

    [Fact]
    public async Task A_staged_upload_to_a_container_that_is_not_there_fails_when_committed()
    {
        await using var upload = Store.BeginUpload("no-such-container", "a.bin");

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await upload.AppendAsync(new byte[] { 1 });
            await upload.CommitAsync("application/octet-stream");
        });
        Assert.False(await Store.ContainerExistsAsync("no-such-container"));
    }

    [Fact]
    public async Task The_size_of_a_missing_blob_or_container_is_null()
    {
        Assert.Null(await Store.GetSizeAsync(Container, "nothing"));
        Assert.Null(await Store.GetSizeAsync("no-such-container", "nothing"));
    }

    [Fact]
    public async Task Creating_a_container_twice_is_not_an_error()
    {
        await Store.CreateContainerAsync("fresh");
        await Store.CreateContainerAsync("fresh");

        Assert.True(await Store.ContainerExistsAsync("fresh"));
    }

    [Fact]
    public async Task The_server_time_is_about_now()
    {
        var time = await Store.GetServerTimeAsync(Container);

        Assert.NotNull(time);
        Assert.InRange(time.Value, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(2));
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
    public async Task Pages_only_through_the_names_that_start_with_the_prefix()
    {
        foreach (var name in new[] { "a/1.json", "b/1.json", "b/2.json", "b/3.json", "c/1.json" })
        {
            await Write(name, "x");
        }

        var names = new List<string>();
        string? token = null;
        var pages = 0;
        do
        {
            var page = await Store.ListPageAsync(Container, "b/", token, pageSize: 2);
            names.AddRange(page.Items.Select(i => i.Name));
            token = page.ContinuationToken;
            pages++;
        }
        while (token is not null && pages < 10);

        Assert.Equal(["b/1.json", "b/2.json", "b/3.json"], names);
    }

    [Fact]
    public async Task A_listing_of_a_container_that_is_not_there_is_empty()
    {
        Assert.Empty(await ToListAsync(Store.ListAsync("no-such-container")));
        Assert.Empty(await ToListAsync(Store.ListAsync("no-such-container", "a/")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_write_to_a_container_that_is_not_there_fails_whatever_the_condition(int kind)
    {
        var condition = kind switch
        {
            0 => BlobWriteCondition.Always,
            1 => BlobWriteCondition.IfNew,
            _ => BlobWriteCondition.IfVersion("1"),
        };

        await Assert.ThrowsAnyAsync<Exception>(
            () => Store.WriteAsync("no-such-container", "a.json", BinaryData.FromString("x"), "text/plain", condition));
        Assert.False(await Store.ContainerExistsAsync("no-such-container"));
    }

    [Fact]
    public async Task Refuses_a_read_url_that_lasts_longer_than_four_hours()
    {
        await Write("a.json", "x");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Store.CreateReadUrlAsync(Container, "a.json", DateTimeOffset.UtcNow.AddHours(4).AddMinutes(5)));
        await Store.CreateReadUrlAsync(Container, "a.json", DateTimeOffset.UtcNow.AddHours(3));
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
    public async Task Deletes_a_blob_only_while_it_still_has_the_version_read()
    {
        var first = await Write("a.json", "first");
        var second = await Write("a.json", "second");

        var lost = await Store.DeleteIfVersionAsync(Container, "a.json", first.Version!);
        var kept = await Store.ReadAsync(Container, "a.json");
        var deleted = await Store.DeleteIfVersionAsync(Container, "a.json", second.Version!);

        Assert.Equal(BlobDeleteStatus.ConditionNotMet, lost);
        Assert.Equal("second", kept!.Content.ToString());
        Assert.Equal(BlobDeleteStatus.Deleted, deleted);
        Assert.Null(await Store.ReadAsync(Container, "a.json"));
    }

    [Fact]
    public async Task A_conditional_delete_of_a_blob_that_is_gone_says_so_and_is_not_an_error()
    {
        var written = await Write("a.json", "first");
        await Store.DeleteAsync(Container, "a.json");

        Assert.Equal(BlobDeleteStatus.NotFound, await Store.DeleteIfVersionAsync(Container, "a.json", written.Version!));
        Assert.Equal(BlobDeleteStatus.NotFound, await Store.DeleteIfVersionAsync(Container, "never-there.json", "v1"));
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
    public async Task Remembers_the_storage_class_a_staged_upload_was_committed_with()
    {
        await using var upload = Memory.BeginUpload(Container, "cold.bin");
        await upload.AppendAsync(new byte[] { 1 });
        await upload.CommitAsync("application/octet-stream", BlobStorageClass.Cold);

        Assert.Equal(BlobStorageClass.Cold, Memory.StorageClassOf(Container, "cold.bin"));
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
