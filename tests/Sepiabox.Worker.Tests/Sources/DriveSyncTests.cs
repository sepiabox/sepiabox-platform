using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Storage;
using Sepiabox.Worker.Cloud;
using Sepiabox.Worker.Sources;

namespace Sepiabox.Worker.Tests.Sources;

/// <summary>
/// <c>sync google-drive</c> on PostgreSQL with a fake Drive (S-038, D-115): what lands in <c>incoming</c>, the cursor between runs, the
/// removal of sources, and what a run does when Google or the store fails. The tokens are real data protection, the files are bytes.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class DriveSyncTests(CatalogFixture fixture)
{
    private const string Account = "ana@example.com";
    private const string SourceName = "ana@example.com (Google Drive)";
    private const string Token = "1//the-refresh-token";
    private static readonly EphemeralDataProtectionProvider s_keys = new();
    private static readonly DateTimeOffset s_modified = new(2020, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_source_name_is_the_address_and_the_service_and_gives_the_address_back_only_for_its_own_service()
    {
        Assert.Equal(SourceName, ConnectedSourceTypes.SourceName(ConnectedSourceTypes.GoogleDrive, Account));
        Assert.Equal(Account, ConnectedSourceTypes.AccountOf(ConnectedSourceTypes.GoogleDrive, SourceName));
        Assert.Null(ConnectedSourceTypes.AccountOf(ConnectedSourceTypes.GoogleDrive, Account));
        Assert.Null(ConnectedSourceTypes.AccountOf(ConnectedSourceTypes.GoogleDrive, " (Google Drive)"));
    }

    [Fact]
    public async Task The_first_sync_of_five_files_puts_five_blobs_in_incoming_in_reference_mode_with_a_new_name_each()
    {
        var (db, store, drive, account) = await SetUpAsync(files: 5);

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal(new DriveSyncResult(1, 5, 0, 0, 0, 0), result);
        var blobs = await store.ListAsync("incoming").ToListAsync();
        Assert.Equal(5, blobs.Count);
        Assert.Equal(5, blobs.Select(b => b.Name).Distinct().Count());
        foreach (var blob in blobs)
        {
            var read = (await store.ReadAsync("incoming", blob.Name))!;
            Assert.True(IncomingFile.TryRead(read.Metadata, out var incoming, out var problem), problem);
            Assert.Equal(("google-drive", SourceName, IncomingMode.Reference), (incoming.SourceType, incoming.SourceName, incoming.Mode));
            Assert.StartsWith("id-", incoming.ExternalId, StringComparison.Ordinal);
            Assert.Equal(s_modified, incoming.ModifiedAt);
            // The bytes are the file's, whole.
            Assert.Equal(drive.Content[incoming.ExternalId!], read.Content.ToArray());
        }

        Assert.Equal(5, await db.DriveFiles.CountAsync(f => f.ConnectedSourceId == account));
        var stored = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.False(stored.SyncListing);
        Assert.Equal(drive.StartToken, stored.SyncChangesToken);
        Assert.NotNull(stored.LastSyncAt);
    }

    [Fact]
    public async Task A_second_sync_with_one_change_puts_one_blob_in_incoming()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 5);
        await Sync(db, store, drive).RunAsync(CancellationToken.None);
        var before = (await store.ListAsync("incoming").ToListAsync()).Select(b => b.Name).ToHashSet();
        drive.Changes.Add(new DriveChange("id-2", false, drive.Files[2] with { ModifiedTime = s_modified.AddDays(1) }));

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Staged);
        var after = await store.ListAsync("incoming").ToListAsync();
        Assert.Equal(6, after.Count);
        Assert.Single(after, b => !before.Contains(b.Name));
    }

    [Fact]
    public async Task Stages_nothing_for_a_file_that_has_not_changed_since_it_was_staged()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 2);
        await Sync(db, store, drive).RunAsync(CancellationToken.None);
        drive.Changes.Add(new DriveChange("id-0", false, drive.Files[0]));

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.Staged);
        Assert.Equal(2, (await store.ListAsync("incoming").ToListAsync()).Count);
    }

    [Fact]
    public async Task A_file_removed_in_drive_marks_its_source_removed_and_keeps_the_source()
    {
        var (db, store, drive, account) = await SetUpAsync(files: 2);
        await Sync(db, store, drive).RunAsync(CancellationToken.None);
        await SeedSourceAsync(db, "Fotos/photo-0.jpg", ContentLength("id-0"));
        drive.Changes.Add(new DriveChange("id-0", true, null));

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Removed);
        var source = await db.AssetSources.AsNoTracking().SingleAsync();
        Assert.NotNull(source.RemovedAt);
        Assert.Equal(1, await db.DriveFiles.CountAsync(f => f.ConnectedSourceId == account));
    }

    [Fact]
    public async Task Marks_only_the_source_of_the_file_that_was_removed_when_two_files_share_a_path()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 1);
        await Sync(db, store, drive).RunAsync(CancellationToken.None);
        // Another asset, from another file of the same name in the same folder, with another size and time.
        await SeedSourceAsync(db, "Fotos/photo-0.jpg", ContentLength("id-0"), 'a');
        await SeedSourceAsync(db, "Fotos/photo-0.jpg", 999, 'b');
        await SeedSourceAsync(db, "Fotos/photo-0.jpg", ContentLength("id-0"), 'd', s_modified.AddDays(5));
        drive.Changes.Add(new DriveChange("id-0", true, null));

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Removed);
        var marked = await db.AssetSources.AsNoTracking().Where(s => s.RemovedAt != null).Select(s => s.AssetId).ToListAsync();
        Assert.Equal(["a"], marked.Select(id => id[..1]).ToList());
    }

    [Fact]
    public async Task A_file_that_comes_back_clears_the_mark()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 1);
        await Sync(db, store, drive).RunAsync(CancellationToken.None);
        await SeedSourceAsync(db, "Fotos/photo-0.jpg", ContentLength("id-0"));
        drive.Changes.Add(new DriveChange("id-0", true, null));
        await Sync(db, store, drive).RunAsync(CancellationToken.None);
        Assert.NotNull((await db.AssetSources.AsNoTracking().SingleAsync()).RemovedAt);
        drive.Changes.Add(new DriveChange("id-0", false, drive.Files[0]));

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Staged);
        Assert.Null((await db.AssetSources.AsNoTracking().SingleAsync()).RemovedAt);
    }

    [Fact]
    public async Task A_file_moved_to_another_folder_is_staged_at_its_new_path_and_the_old_source_is_marked_removed()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 1);
        await Sync(db, store, drive).RunAsync(CancellationToken.None);
        await SeedSourceAsync(db, "Fotos/photo-0.jpg", ContentLength("id-0"));
        drive.Files[0] = drive.Files[0] with { Parents = ["folder-2"], ModifiedTime = s_modified.AddHours(1) };
        drive.Changes.Add(new DriveChange("id-0", false, drive.Files[0]));

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal((1, 1), (result.Staged, result.Removed));
        Assert.Equal("Viagem/photo-0.jpg", (await db.DriveFiles.AsNoTracking().SingleAsync()).Path);
        Assert.NotNull((await db.AssetSources.AsNoTracking().SingleAsync()).RemovedAt);
    }

    [Fact]
    public async Task Lists_a_few_files_a_run_and_goes_on_from_the_page_it_stopped_at_without_staging_a_file_twice()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 7);

        var first = await Sync(db, store, drive, maxFilesPerRun: 2).RunAsync(CancellationToken.None);
        var stored = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.True(stored.SyncListing);
        Assert.NotNull(stored.SyncListToken);
        var second = await Sync(db, store, drive, maxFilesPerRun: 2).RunAsync(CancellationToken.None);
        var third = await Sync(db, store, drive, maxFilesPerRun: 2).RunAsync(CancellationToken.None);
        var fourth = await Sync(db, store, drive, maxFilesPerRun: 2).RunAsync(CancellationToken.None);

        // A page is two files (the fake's), so a budget of two is a page a run.
        Assert.Equal([2, 2, 2, 1], [first.Staged, second.Staged, third.Staged, fourth.Staged]);
        Assert.Equal(7, (await store.ListAsync("incoming").ToListAsync()).Count);
        Assert.False((await db.ConnectedSources.AsNoTracking().SingleAsync()).SyncListing);
    }

    [Fact]
    public async Task Gives_a_file_without_an_extension_one_from_its_type_and_leaves_out_one_that_is_neither_photo_nor_video()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 0);
        drive.AddFile("id-a", "scan", "image/jpeg");
        drive.AddFile("id-b", "weird", "image/x-weird");

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal((1, 1), (result.Staged, result.Skipped));
        Assert.Equal("scan.jpg", (await db.DriveFiles.AsNoTracking().SingleAsync()).Path);
    }

    [Fact]
    public async Task Leaves_out_a_file_whose_path_would_not_fit_a_blobs_metadata_and_goes_on_with_the_others()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 1);
        drive.AddFile("id-long", new string('n', 9000) + ".jpg", "image/jpeg");

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal((1, 1), (result.Staged, result.Skipped));
        Assert.Single(await store.ListAsync("incoming").ToListAsync());
    }

    [Fact]
    public async Task Leaves_out_a_file_larger_than_the_worker_takes_without_downloading_it()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 0);
        drive.AddFile("id-big", "big.mp4", "video/mp4", size: CloudFileProcessor.MaxFileBytes + 1);

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal((0, 1), (result.Staged, result.Skipped));
        Assert.Empty(drive.Downloaded);
    }

    [Theory]
    [InlineData("https://drive.google.com/file/d/1/view", "https://drive.google.com/file/d/1/view")]
    [InlineData("http://drive.google.com/file/d/1/view", null)]
    [InlineData("https://user:pw@drive.google.com/file/d/1/view", null)]
    [InlineData(null, null)]
    public async Task Gives_the_blob_the_files_address_only_when_it_is_an_https_one(string? link, string? expected)
    {
        var (db, store, drive, _) = await SetUpAsync(files: 0);
        drive.AddFile("id-a", "a.jpg", "image/jpeg", webViewLink: link);

        await Sync(db, store, drive).RunAsync(CancellationToken.None);

        var blob = await store.ListAsync("incoming").SingleAsync();
        Assert.True(IncomingFile.TryRead((await store.ReadAsync("incoming", blob.Name))!.Metadata, out var incoming, out _));
        Assert.Equal(expected, incoming.Url);
    }

    [Fact]
    public async Task A_token_google_refuses_makes_the_account_reconnect_and_stages_nothing()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 3);
        drive.Failure = () => new DriveAuthorizationException("invalid_grant");

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal((1, 0, 1), (result.Accounts, result.Staged, result.Reconnect));
        Assert.Equal(ConnectedSourceStatuses.Reconnect, (await db.ConnectedSources.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await store.ListAsync("incoming").ToListAsync());
        // A later run leaves the account alone until the family connects it again.
        Assert.Equal(0, (await Sync(db, store, new FakeDriveSync()).RunAsync(CancellationToken.None)).Accounts);
    }

    [Fact]
    public async Task A_timeout_stops_that_account_without_reconnecting_it_and_the_next_account_still_runs()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 2);
        db.ConnectedSources.Add(new ConnectedSource
        {
            Type = ConnectedSourceTypes.GoogleDrive,
            AccountEmail = "bia@example.com",
            EncryptedRefreshToken = s_keys.CreateProtector(ConnectedSource.RefreshTokenPurpose).Protect(Token),
            Status = ConnectedSourceStatuses.Connected,
            ConnectedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        // The first account's first call times out (an HttpClient timeout is a TaskCanceledException); the second account's is fine.
        var calls = 0;
        drive.Failure = () => ++calls == 1 ? new TaskCanceledException("timed out") : null;

        var result = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal((2, 1, 0), (result.Accounts, result.Failed, result.Reconnect));
        Assert.Equal(2, (await store.ListAsync("incoming").ToListAsync()).Count);
        Assert.All(await db.ConnectedSources.AsNoTracking().ToListAsync(), s => Assert.Equal(ConnectedSourceStatuses.Connected, s.Status));
    }

    [Fact]
    public async Task A_key_ring_that_cannot_be_read_just_now_fails_the_run_and_leaves_the_account_connected()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 2);

        var result = await new DriveSync(db, store, drive, new FailingKeys(), TimeProvider.System, NullLogger.Instance).RunAsync(CancellationToken.None);

        Assert.Equal((1, 0), (result.Failed, result.Reconnect));
        Assert.Equal(ConnectedSourceStatuses.Connected, (await db.ConnectedSources.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(drive.TokensUsed);
    }

    [Fact]
    public async Task Rescan_forgets_the_cursor_and_the_staged_files_so_the_next_run_stages_everything_again()
    {
        var (db, store, drive, account) = await SetUpAsync(files: 3);
        await Sync(db, store, drive).RunAsync(CancellationToken.None);
        Assert.Equal(3, await db.DriveFiles.CountAsync(f => f.ConnectedSourceId == account));

        var found = await Sync(db, store, drive).RescanAsync(" Ana@Example.com ", CancellationToken.None);

        Assert.True(found);
        Assert.Equal(0, await db.DriveFiles.CountAsync(f => f.ConnectedSourceId == account));
        var stored = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.Equal((null, null, false), (stored.SyncChangesToken, stored.SyncListToken, stored.SyncListing));
        var again = await Sync(db, store, drive).RunAsync(CancellationToken.None);
        Assert.Equal(3, again.Staged);
        Assert.Equal(6, (await store.ListAsync("incoming").ToListAsync()).Count);
        Assert.False(await Sync(db, store, drive).RescanAsync("nobody@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task A_token_the_key_ring_cannot_read_makes_the_account_reconnect()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 3);

        var result = await new DriveSync(db, store, drive, new EphemeralDataProtectionProvider(), TimeProvider.System, NullLogger.Instance)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Reconnect);
        Assert.Empty(drive.TokensUsed);
    }

    [Fact]
    public async Task A_download_that_fails_stops_the_account_leaves_the_cursor_and_the_next_run_stages_the_rest_once()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 4);
        drive.FailDownloadOf = "id-2";

        var first = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal(1, first.Failed);
        var stored = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.True(stored.SyncListing);
        // The blob of the failed download was never committed.
        Assert.Equal(2, (await store.ListAsync("incoming").ToListAsync()).Count);

        drive.FailDownloadOf = null;
        var second = await Sync(db, store, drive).RunAsync(CancellationToken.None);

        Assert.Equal(0, second.Failed);
        Assert.Equal(4, (await store.ListAsync("incoming").ToListAsync()).Count);
        Assert.Equal(4, await db.DriveFiles.CountAsync());
    }

    [Fact]
    public async Task Never_logs_a_token_a_file_name_or_an_address()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 2);
        drive.AddFile("id-bad", "secret-holiday.jpg", "image/jpeg", size: CloudFileProcessor.MaxFileBytes + 1, webViewLink: "https://drive.google.com/file/d/secret/view");
        var log = new ListLogger();

        await new DriveSync(db, store, drive, s_keys, TimeProvider.System, log).RunAsync(CancellationToken.None);

        var text = string.Join('\n', log.Messages);
        Assert.NotEmpty(log.Messages);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("photo-0", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Account, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Content_a_drive_file_shares_with_an_asset_from_the_hd_gains_a_source_and_makes_no_second_asset()
    {
        var (db, store, drive, _) = await SetUpAsync(files: 0);
        var bytes = TestPhotos.Porto("silver");
        drive.AddFile("id-same", "Praia.jpg", "image/jpeg", content: bytes);
        var id = MediaFile.Sha256Hex(bytes);
        var manifests = new ManifestStore(new ManifestValidator(), store);
        var hd = new Manifest
        {
            Id = id,
            Kind = MediaKind.Photo,
            SizeBytes = bytes.Length,
            ContentType = "image/jpeg",
            Sources = [new ManifestSource("local", "HD da Ana", "2020/praia.jpg", s_modified)],
            Derivatives = [new ManifestDerivative("thumb-256", $"derivatives/{id}/thumb-256.webp", 256, 192, "image/webp")],
        };
        await manifests.WriteMergedAsync("manifests", hd, CancellationToken.None);
        await store.WriteAsync("derivatives", $"{id}/thumb-256.webp", BinaryData.FromBytes([1]), "image/webp", BlobWriteCondition.Always);
        await Sync(db, store, drive).RunAsync(CancellationToken.None);
        var blob = await store.ListAsync("incoming").SingleAsync();

        var processed = await new CloudFileProcessor(store, manifests, null, null, null, TimeProvider.System, NullLogger.Instance)
            .ProcessAsync(blob.Name, CancellationToken.None);

        Assert.Equal(CloudFileStatus.Processed, processed.Status);
        var merged = ManifestJson.Deserialize((await store.ReadAsync("manifests", BlobLayout.ManifestBlobName(id)))!.Content.ToString());
        Assert.Equal(["google-drive", "local"], merged.Sources.Select(s => s.Type).Order());
        Assert.Single(await store.ListAsync("manifests").ToListAsync());
        // The HD stored the original; the Drive source adds none and takes nothing away.
        Assert.True(merged.IsOriginalStored);
        Assert.Empty(await store.ListAsync("originals").ToListAsync());
    }

    private static DriveSync Sync(CatalogDbContext db, IBlobStore store, IDriveSyncClient drive, int maxFilesPerRun = DriveSync.DefaultMaxFilesPerRun) =>
        new(db, store, drive, s_keys, TimeProvider.System, NullLogger.Instance, maxFilesPerRun);

    private async Task<(CatalogDbContext Db, InMemoryBlobStore Store, FakeDriveSync Drive, long Account)> SetUpAsync(int files)
    {
        var db = await fixture.CreateMigratedDatabaseAsync();
        var store = new InMemoryBlobStore();
        foreach (var container in new[] { "incoming", "manifests", "derivatives", "originals" })
        {
            store.CreateContainer(container);
        }

        var account = new ConnectedSource
        {
            Type = ConnectedSourceTypes.GoogleDrive,
            AccountEmail = Account,
            EncryptedRefreshToken = s_keys.CreateProtector(ConnectedSource.RefreshTokenPurpose).Protect(Token),
            Status = ConnectedSourceStatuses.Connected,
            ConnectedAt = DateTimeOffset.UtcNow,
        };
        db.ConnectedSources.Add(account);
        await db.SaveChangesAsync();
        var drive = new FakeDriveSync();
        for (var i = 0; i < files; i++)
        {
            drive.AddFile($"id-{i}", $"photo-{i}.jpg", "image/jpeg", parent: "folder-1");
        }

        drive.Folders["folder-1"] = new DriveFolder("Fotos", "root-child");
        drive.Folders["folder-2"] = new DriveFolder("Viagem", "root-child");
        drive.Folders["root-child"] = new DriveFolder("My Drive", null);
        return (db, store, drive, account.Id);
    }

    // What the fake Drive's file of that id holds.
    private static long ContentLength(string id) => System.Text.Encoding.UTF8.GetByteCount("content of " + id);

    private static async Task SeedSourceAsync(CatalogDbContext db, string path, long size, char id = 'c', DateTimeOffset? modifiedAt = null)
    {
        var assetId = new string(id, 64);
        db.Assets.Add(new Asset
        {
            Id = assetId,
            Kind = "photo",
            ContentType = "image/jpeg",
            SizeBytes = size,
            Sources = [new AssetSource { AssetId = assetId, Type = "google-drive", Name = SourceName, Path = path, ModifiedAt = modifiedAt ?? s_modified }],
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    // A key ring whose storage fails: not a missing key, which would be a CryptographicException.
    private sealed class FailingKeys : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new Failing();

        private sealed class Failing : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => this;

            public byte[] Protect(byte[] plaintext) => throw new IOException("the key ring's storage is down");

            public byte[] Unprotect(byte[] protectedData) => throw new IOException("the key ring's storage is down");
        }
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }
}

/// <summary>A Drive that never reaches Google: a list of files in pages of two, a queue of changes, folders and the files' bytes.</summary>
internal sealed class FakeDriveSync : IDriveSyncClient
{
    private static readonly DateTimeOffset s_modified = new(2020, 5, 1, 10, 0, 0, TimeSpan.Zero);

    public List<DriveFileInfo> Files { get; } = [];

    public Dictionary<string, byte[]> Content { get; } = [];

    public Dictionary<string, DriveFolder> Folders { get; } = [];

    public List<DriveChange> Changes { get; } = [];

    public string StartToken { get; } = "start-1";

    public List<string> TokensUsed { get; } = [];

    public List<string> Downloaded { get; } = [];

    public string? FailDownloadOf { get; set; }

    public Func<Exception?>? Failure { get; set; }

    public void AddFile(
        string id, string name, string mimeType, long? size = null, string? webViewLink = null, string? parent = null, byte[]? content = null)
    {
        content ??= System.Text.Encoding.UTF8.GetBytes("content of " + id);
        Files.Add(new DriveFileInfo(id, name, mimeType, size ?? content.Length, s_modified, "md5-" + id, parent is null ? [] : [parent], webViewLink));
        Content[id] = content;
    }

    public Task<string> GetStartPageTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        Use(refreshToken);
        return Task.FromResult(StartToken);
    }

    // Pages of two, the page token being the index to go on from.
    public Task<DriveListPage> ListMediaAsync(string refreshToken, string? pageToken, CancellationToken cancellationToken = default)
    {
        Use(refreshToken);
        var from = pageToken is null ? 0 : int.Parse(pageToken, System.Globalization.CultureInfo.InvariantCulture);
        var page = Files.Skip(from).Take(2).ToList();
        var next = from + 2 < Files.Count ? (from + 2).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new DriveListPage(page, next));
    }

    public Task<DriveChangesPage> ListChangesAsync(string refreshToken, string pageToken, CancellationToken cancellationToken = default)
    {
        Use(refreshToken);
        var changes = Changes.ToList();
        Changes.Clear();
        return Task.FromResult(new DriveChangesPage(changes, null, "start-2"));
    }

    public Task<DriveFolder?> GetFolderAsync(string refreshToken, string folderId, CancellationToken cancellationToken = default)
    {
        Use(refreshToken);
        return Task.FromResult(Folders.GetValueOrDefault(folderId));
    }

    public async Task DownloadAsync(string refreshToken, string fileId, Stream destination, CancellationToken cancellationToken = default)
    {
        Use(refreshToken);
        if (fileId == FailDownloadOf)
        {
            // Some of the bytes have gone when it fails, as a dropped connection leaves it.
            await destination.WriteAsync(Content[fileId].AsMemory(0, 2), cancellationToken);
            throw new IOException("the connection dropped");
        }

        Downloaded.Add(fileId);
        await destination.WriteAsync(Content[fileId], cancellationToken);
    }

    private void Use(string refreshToken)
    {
        TokensUsed.Add(refreshToken);
        if (Failure?.Invoke() is { } failure)
        {
            throw failure;
        }
    }
}
