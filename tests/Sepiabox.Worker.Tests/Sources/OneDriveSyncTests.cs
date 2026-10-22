using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Storage;
using Sepiabox.Worker.Sources;

namespace Sepiabox.Worker.Tests.Sources;

/// <summary>
/// <c>sync onedrive</c> on PostgreSQL with a fake OneDrive (S-039, D-116): what lands in <c>incoming</c>, the delta link between runs,
/// the refresh token Microsoft rolls, the removal of sources and what a run does when Microsoft or the store fails.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class OneDriveSyncTests(CatalogFixture fixture)
{
    private const string Account = "ana@outlook.com";
    private const string SourceName = "ana@outlook.com (OneDrive)";
    private const string Token = "M.the-first-refresh-token";
    private static readonly EphemeralDataProtectionProvider s_keys = new();
    private static readonly DateTimeOffset s_modified = new(2021, 6, 2, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_sync_puts_every_photo_in_incoming_in_reference_mode_and_keeps_the_delta_link()
    {
        var (db, store, onedrive, account) = await SetUpAsync(files: 5);

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal(new DriveSyncResult(1, 5, 0, 0, 0, 0), result);
        var blobs = await store.ListAsync("incoming").ToListAsync();
        Assert.Equal(5, blobs.Count);
        foreach (var blob in blobs)
        {
            var read = (await store.ReadAsync("incoming", blob.Name))!;
            Assert.True(IncomingFile.TryRead(read.Metadata, out var incoming, out var problem), problem);
            Assert.Equal(("onedrive", SourceName, IncomingMode.Reference), (incoming.SourceType, incoming.SourceName, incoming.Mode));
            Assert.StartsWith("Fotos/photo-", incoming.Path, StringComparison.Ordinal);
            Assert.Equal(onedrive.Content[incoming.ExternalId!], read.Content.ToArray());
        }

        Assert.Equal(5, await db.DriveFiles.CountAsync(f => f.ConnectedSourceId == account));
        var stored = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.Equal((onedrive.DeltaLink, null, false), (stored.SyncChangesToken, stored.SyncListToken, stored.SyncListing));
        Assert.NotNull(stored.LastSyncAt);
    }

    [Fact]
    public async Task A_large_onedrive_is_spread_over_runs_and_the_next_run_goes_on_from_the_page_it_stopped_at()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 5);

        var first = await Sync(db, store, onedrive, maxFilesPerRun: 2).RunAsync(CancellationToken.None);

        Assert.Equal(2, first.Staged);
        var midway = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.True(midway.SyncListing);
        Assert.NotNull(midway.SyncListToken);
        var second = await Sync(db, store, onedrive, maxFilesPerRun: 2).RunAsync(CancellationToken.None);
        var third = await Sync(db, store, onedrive, maxFilesPerRun: 2).RunAsync(CancellationToken.None);
        Assert.Equal(5, first.Staged + second.Staged + third.Staged);
        Assert.Equal(5, (await store.ListAsync("incoming").ToListAsync()).Count);
        Assert.False((await db.ConnectedSources.AsNoTracking().SingleAsync()).SyncListing);
    }

    [Fact]
    public async Task A_later_run_reads_only_the_changes_and_stages_nothing_for_a_file_that_did_not_change()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 3);
        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);
        onedrive.Changes.Add(new OneDriveChange("id-0", false, onedrive.Files[0]));
        onedrive.Changes.Add(new OneDriveChange("id-1", false, onedrive.Files[1] with { ModifiedTime = s_modified.AddDays(1) }));

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Staged);
        Assert.Equal(4, (await store.ListAsync("incoming").ToListAsync()).Count);
    }

    [Fact]
    public async Task A_delta_link_microsoft_no_longer_honours_lists_the_onedrive_again_and_stages_only_what_is_new()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 2);
        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);
        onedrive.AddFile("id-new", "new.jpg", "image/jpeg");
        onedrive.ResyncOnNextDelta = true;

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal((1, 1, 0), (result.Staged, result.Accounts, result.Failed));
        Assert.Equal(3, (await store.ListAsync("incoming").ToListAsync()).Count);
        Assert.Equal(1, onedrive.FullListings - 1);
        Assert.NotNull((await db.ConnectedSources.AsNoTracking().SingleAsync()).SyncChangesToken);
    }

    [Fact]
    public async Task A_file_with_a_type_graph_does_not_know_is_staged_by_its_extension_and_counted_as_skipped_when_there_is_none()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 0);
        onedrive.AddFile("id-raw", "IMG_0001.CR2", "application/octet-stream");
        onedrive.AddFile("id-mystery", "mystery", "application/octet-stream");

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal((1, 1), (result.Staged, result.Skipped));
    }

    [Fact]
    public async Task Names_only_the_first_twenty_files_left_out_for_their_names_and_counts_the_rest()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 0);
        for (var i = 0; i < 25; i++)
        {
            onedrive.AddFile($"id-x{i}", $"mystery-{i}", "application/octet-stream");
        }

        var log = new ListLogger();
        var result = await new OneDriveSync(db, store, onedrive, s_keys, TimeProvider.System, log).RunAsync(CancellationToken.None);

        Assert.Equal(25, result.Skipped);
        Assert.Equal(20, log.Messages.Count(m => m.Contains("not a photo's or video's", StringComparison.Ordinal) && !m.Contains("more file", StringComparison.Ordinal)));
        Assert.Single(log.Messages, m => m.StartsWith("5 more file(s)", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Messages, m => m.Contains("mystery", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rescan_forgets_the_delta_state_and_the_staged_files_of_that_onedrive_only()
    {
        var (db, store, onedrive, account) = await SetUpAsync(files: 3);
        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.True(await Sync(db, store, onedrive).RescanAsync(" Ana@Outlook.com ", CancellationToken.None));

        Assert.Equal(0, await db.DriveFiles.CountAsync(f => f.ConnectedSourceId == account));
        Assert.Null((await db.ConnectedSources.AsNoTracking().SingleAsync()).SyncChangesToken);
        Assert.Equal(3, (await Sync(db, store, onedrive).RunAsync(CancellationToken.None)).Staged);
        Assert.False(await Sync(db, store, onedrive).RescanAsync("nobody@outlook.com", CancellationToken.None));
    }

    [Fact]
    public async Task A_refresh_token_microsoft_rolled_is_stored_protected_and_used_the_next_time()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 1);
        onedrive.RolledTo = "M.the-second-refresh-token";

        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);
        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        var row = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("second", row.EncryptedRefreshToken, StringComparison.Ordinal);
        Assert.Equal("M.the-second-refresh-token", Encoding.UTF8.GetString(s_keys.CreateProtector(ConnectedSource.RefreshTokenPurpose).Unprotect(WebEncoders.Base64UrlDecode(row.EncryptedRefreshToken))));
        Assert.Equal([Token, "M.the-second-refresh-token"], onedrive.Refreshed);
    }

    [Fact]
    public async Task The_rolled_token_is_kept_even_when_the_run_then_fails()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 3);
        onedrive.RolledTo = "M.rolled-before-the-failure";
        onedrive.FailDownloadOf = "id-1";

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Failed);
        var row = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.Equal(ConnectedSourceStatuses.Connected, row.Status);
        Assert.Equal("M.rolled-before-the-failure", Encoding.UTF8.GetString(s_keys.CreateProtector(ConnectedSource.RefreshTokenPurpose).Unprotect(WebEncoders.Base64UrlDecode(row.EncryptedRefreshToken))));
        // What was staged before the failure is known, so the next run stages only the rest.
        onedrive.FailDownloadOf = null;
        var next = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);
        Assert.Equal(3, (await store.ListAsync("incoming").ToListAsync()).Count);
        Assert.Equal(2, next.Staged);
    }

    [Fact]
    public async Task A_file_deleted_in_onedrive_marks_only_its_source_removed()
    {
        var (db, store, onedrive, account) = await SetUpAsync(files: 2);
        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);
        await SeedSourceAsync(db, "Fotos/photo-0.jpg", ContentLength("id-0"));
        onedrive.Changes.Add(new OneDriveChange("id-0", true, null));

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Removed);
        Assert.NotNull((await db.AssetSources.AsNoTracking().SingleAsync()).RemovedAt);
        Assert.Equal(1, await db.DriveFiles.CountAsync(f => f.ConnectedSourceId == account));
    }

    [Fact]
    public async Task A_moved_file_is_staged_at_its_new_path_and_its_old_source_is_marked_removed()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 1);
        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);
        await SeedSourceAsync(db, "Fotos/photo-0.jpg", ContentLength("id-0"));
        onedrive.Changes.Add(new OneDriveChange("id-0", false, onedrive.Files[0] with { ParentId = "folder-2", ModifiedTime = s_modified.AddDays(1) }));

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal((1, 1), (result.Staged, result.Removed));
        Assert.Equal("Viagem/photo-0.jpg", (await db.DriveFiles.AsNoTracking().SingleAsync()).Path);
    }

    [Fact]
    public async Task A_token_microsoft_refuses_makes_the_account_reconnect_and_the_other_failures_do_not()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 2);
        onedrive.RefreshFailure = new HttpRequestException("down");

        var down = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal((1, 0), (down.Failed, down.Reconnect));
        Assert.Equal(ConnectedSourceStatuses.Connected, (await db.ConnectedSources.AsNoTracking().SingleAsync()).Status);
        onedrive.RefreshFailure = new CloudAuthorizationException("invalid_grant");

        var refused = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal((0, 1), (refused.Failed, refused.Reconnect));
        Assert.Equal(ConnectedSourceStatuses.Reconnect, (await db.ConnectedSources.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await store.ListAsync("incoming").ToListAsync());
    }

    [Fact]
    public async Task A_token_the_key_ring_cannot_read_makes_the_account_reconnect_without_asking_microsoft()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 1);

        var result = await new OneDriveSync(db, store, onedrive, new EphemeralDataProtectionProvider(), TimeProvider.System, NullLogger.Instance)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Reconnect);
        Assert.Empty(onedrive.Refreshed);
    }

    [Fact]
    public async Task A_name_that_is_not_a_photo_or_video_is_left_out_and_counted()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 0);
        onedrive.AddFile("id-doc", "notes.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        onedrive.AddFile("id-pic", "pic.jpg", "image/jpeg");

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal((1, 1), (result.Staged, result.Skipped));
    }

    private const string StoredDelta = "https://graph.microsoft.com/v1.0/me/drive/root/delta?token=stored";

    [Fact]
    public async Task Takes_the_list_link_before_the_changes_link_when_both_are_set()
    {
        var (db, store, onedrive, account) = await SetUpAsync(files: 5);
        await SetCursorAsync(db, account, changes: StoredDelta, list: "https://graph.microsoft.com/v1.0/me/drive/root/delta?page=2", listing: true);

        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal("https://graph.microsoft.com/v1.0/me/drive/root/delta?page=2", onedrive.Links[0]);
        Assert.DoesNotContain(StoredDelta, onedrive.Links);
    }

    [Fact]
    public async Task Resumes_a_listing_mid_way_staging_the_rest_and_then_asks_from_the_delta_link()
    {
        var (db, store, onedrive, account) = await SetUpAsync(files: 5);
        await SetCursorAsync(db, account, changes: null, list: "https://graph.microsoft.com/v1.0/me/drive/root/delta?page=2", listing: true);

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        // The first two files were staged by an earlier run: only the last three are now.
        Assert.Equal(3, result.Staged);
        Assert.Equal(["https://graph.microsoft.com/v1.0/me/drive/root/delta?page=2", "https://graph.microsoft.com/v1.0/me/drive/root/delta?page=4"], onedrive.Links.Select(l => l!));
        var stored = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.Equal((onedrive.DeltaLink, null, false), (stored.SyncChangesToken, stored.SyncListToken, stored.SyncListing));
    }

    [Fact]
    public async Task A_last_page_with_no_delta_link_keeps_the_one_already_stored_and_ends_the_listing()
    {
        var (db, store, onedrive, account) = await SetUpAsync(files: 1);
        await SetCursorAsync(db, account, changes: StoredDelta, list: null, listing: false);
        onedrive.NoDeltaLink = true;
        onedrive.Changes.Add(new OneDriveChange("id-0", false, onedrive.Files[0]));

        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        var stored = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.Equal((StoredDelta, null, false), (stored.SyncChangesToken, stored.SyncListToken, stored.SyncListing));
    }

    [Fact]
    public async Task A_token_microsoft_did_not_roll_is_not_written_again()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 1);
        var before = (await db.ConnectedSources.AsNoTracking().SingleAsync()).EncryptedRefreshToken;
        onedrive.RolledTo = Token;

        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal(before, (await db.ConnectedSources.AsNoTracking().SingleAsync()).EncryptedRefreshToken);
    }

    [Fact]
    public async Task Skips_an_account_that_needs_connecting_again_and_leaves_a_google_account_alone()
    {
        var (db, store, onedrive, account) = await SetUpAsync(files: 2);
        (await db.ConnectedSources.SingleAsync(s => s.Id == account)).Status = ConnectedSourceStatuses.Reconnect;
        db.ConnectedSources.Add(new ConnectedSource
        {
            Type = ConnectedSourceTypes.GoogleDrive,
            AccountEmail = "ana@example.com",
            EncryptedRefreshToken = s_keys.CreateProtector(ConnectedSource.RefreshTokenPurpose).Protect("google-token"),
            Status = ConnectedSourceStatuses.Connected,
            ConnectedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal(new DriveSyncResult(0, 0, 0, 0, 0, 0), result);
        Assert.Empty(onedrive.Refreshed);
        var google = await db.ConnectedSources.AsNoTracking().SingleAsync(s => s.Type == ConnectedSourceTypes.GoogleDrive);
        Assert.Equal((null, ConnectedSourceStatuses.Connected), (google.SyncChangesToken, google.Status));
    }

    [Fact]
    public async Task Rescan_of_a_onedrive_leaves_a_google_drive_with_the_same_address_alone()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 1);
        var google = new ConnectedSource
        {
            Type = ConnectedSourceTypes.GoogleDrive,
            AccountEmail = Account,
            EncryptedRefreshToken = s_keys.CreateProtector(ConnectedSource.RefreshTokenPurpose).Protect("google-token"),
            Status = ConnectedSourceStatuses.Connected,
            ConnectedAt = DateTimeOffset.UtcNow,
            SyncChangesToken = "google-cursor",
        };
        db.ConnectedSources.Add(google);
        await db.SaveChangesAsync();
        db.DriveFiles.Add(new DriveFile { ConnectedSourceId = google.Id, FileId = "g-1", Checksum = "h", Path = "Fotos/g.jpg", SizeBytes = 1 });
        await db.SaveChangesAsync();

        Assert.True(await Sync(db, store, onedrive).RescanAsync(Account, CancellationToken.None));

        Assert.Equal("google-cursor", (await db.ConnectedSources.AsNoTracking().SingleAsync(s => s.Id == google.Id)).SyncChangesToken);
        Assert.Equal(1, await db.DriveFiles.CountAsync(f => f.ConnectedSourceId == google.Id));
    }

    [Fact]
    public async Task A_file_is_not_staged_again_only_when_both_its_hash_and_its_path_are_what_they_were()
    {
        var (db, store, onedrive, _) = await SetUpAsync(files: 2);
        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);
        // The same content moved to another folder, and the same place with other content: each is staged.
        onedrive.Changes.Add(new OneDriveChange("id-0", false, onedrive.Files[0] with { ParentId = "folder-2" }));
        onedrive.Changes.Add(new OneDriveChange("id-1", false, onedrive.Files[1] with { Hash = "another-hash" }));

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal(2, result.Staged);
        // And the file whose hash and path are both unchanged is not.
        onedrive.Changes.Add(new OneDriveChange("id-1", false, onedrive.Files[1] with { Hash = "another-hash" }));
        Assert.Equal(0, (await Sync(db, store, onedrive).RunAsync(CancellationToken.None)).Staged);
    }

    [Fact]
    public async Task A_change_that_says_removed_is_a_removal_even_when_it_carries_a_file()
    {
        var (db, store, onedrive, account) = await SetUpAsync(files: 2);
        await Sync(db, store, onedrive).RunAsync(CancellationToken.None);
        await SeedSourceAsync(db, "Fotos/photo-0.jpg", ContentLength("id-0"));
        onedrive.Changes.Add(new OneDriveChange("id-0", true, onedrive.Files[0] with { ModifiedTime = s_modified.AddDays(3) }));

        var result = await Sync(db, store, onedrive).RunAsync(CancellationToken.None);

        Assert.Equal((0, 1), (result.Staged, result.Removed));
        Assert.Equal(1, await db.DriveFiles.CountAsync(f => f.ConnectedSourceId == account));
    }

    private static async Task SetCursorAsync(CatalogDbContext db, long account, string? changes, string? list, bool listing)
    {
        var row = await db.ConnectedSources.SingleAsync(s => s.Id == account);
        (row.SyncChangesToken, row.SyncListToken, row.SyncListing) = (changes, list, listing);
        await db.SaveChangesAsync();
    }

    private static OneDriveSync Sync(CatalogDbContext db, IBlobStore store, IOneDriveSyncClient client, int maxFilesPerRun = DriveSync.DefaultMaxFilesPerRun) =>
        new(db, store, client, s_keys, TimeProvider.System, NullLogger.Instance, maxFilesPerRun);

    private async Task<(CatalogDbContext Db, InMemoryBlobStore Store, FakeOneDriveSync OneDrive, long Account)> SetUpAsync(int files)
    {
        var db = await fixture.CreateMigratedDatabaseAsync();
        var store = new InMemoryBlobStore();
        foreach (var container in new[] { "incoming", "manifests", "derivatives", "originals" })
        {
            store.CreateContainer(container);
        }

        var account = new ConnectedSource
        {
            Type = ConnectedSourceTypes.OneDrive,
            AccountEmail = Account,
            EncryptedRefreshToken = s_keys.CreateProtector(ConnectedSource.RefreshTokenPurpose).Protect(Token),
            Status = ConnectedSourceStatuses.Connected,
            ConnectedAt = DateTimeOffset.UtcNow,
        };
        db.ConnectedSources.Add(account);
        await db.SaveChangesAsync();
        var onedrive = new FakeOneDriveSync();
        for (var i = 0; i < files; i++)
        {
            onedrive.AddFile($"id-{i}", $"photo-{i}.jpg", "image/jpeg");
        }

        return (db, store, onedrive, account.Id);
    }

    private static long ContentLength(string id) => Encoding.UTF8.GetByteCount("content of " + id);

    private static async Task SeedSourceAsync(CatalogDbContext db, string path, long size)
    {
        var assetId = new string('c', 64);
        db.Assets.Add(new Asset
        {
            Id = assetId,
            Kind = "photo",
            ContentType = "image/jpeg",
            SizeBytes = size,
            Sources = [new AssetSource { AssetId = assetId, Type = "onedrive", Name = SourceName, Path = path, ModifiedAt = s_modified }],
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}

internal sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception));
}

/// <summary>A OneDrive that never reaches Microsoft: files listed in delta pages of two, a queue of later changes, folders and bytes.</summary>
internal sealed class FakeOneDriveSync : IOneDriveSyncClient
{
    private static readonly DateTimeOffset s_modified = new(2021, 6, 2, 11, 0, 0, TimeSpan.Zero);

    public List<OneDriveItem> Files { get; } = [];

    public Dictionary<string, byte[]> Content { get; } = [];

    public Dictionary<string, OneDriveFolder> Folders { get; } = new()
    {
        ["folder-1"] = new OneDriveFolder("Fotos", "root"),
        ["folder-2"] = new OneDriveFolder("Viagem", "root"),
        ["root"] = new OneDriveFolder("root", null),
    };

    /// <summary>Changes the next delta from the delta link returns (and then clears).</summary>
    public List<OneDriveChange> Changes { get; } = [];

    public string DeltaLink { get; private set; } = "https://graph.microsoft.com/v1.0/me/drive/root/delta?token=d0";

    public string? RolledTo { get; set; }

    public Exception? RefreshFailure { get; set; }

    public string? FailDownloadOf { get; set; }

    /// <summary>The next delta from a delta link answers 410 once.</summary>
    public bool ResyncOnNextDelta { get; set; }

    /// <summary>How many deltas started from the beginning.</summary>
    public int FullListings { get; private set; }

    public List<string> Refreshed { get; } = [];

    /// <summary>The links the deltas were asked from, in order (null for the start).</summary>
    public List<string?> Links { get; } = [];

    /// <summary>The last page of a listing comes with no delta link (Graph may do that).</summary>
    public bool NoDeltaLink { get; set; }

    public void AddFile(string id, string name, string mimeType, string parent = "folder-1")
    {
        Files.Add(new OneDriveItem(id, name, mimeType, Encoding.UTF8.GetByteCount("content of " + id), s_modified, "hash-" + id, parent, "https://onedrive.live.com/?id=" + id));
        Content[id] = Encoding.UTF8.GetBytes("content of " + id);
    }

    public Task<OneDriveToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        Refreshed.Add(refreshToken);
        // The one refresh rolls the token once; later ones keep what they are given.
        return RefreshFailure is { } failure
            ? Task.FromException<OneDriveToken>(failure)
            : Task.FromResult(new OneDriveToken("access-token", RolledTo));
    }

    public Task<OneDriveDeltaPage> DeltaAsync(string accessToken, string? link, CancellationToken cancellationToken = default)
    {
        Links.Add(link);
        if (link is not null && !link.Contains("page=", StringComparison.Ordinal) && ResyncOnNextDelta)
        {
            ResyncOnNextDelta = false;
            throw new DeltaResyncRequiredException();
        }

        if (link is null)
        {
            FullListings++;
        }

        if (link is null || link.Contains("page=", StringComparison.Ordinal))
        {
            var start = link is null ? 0 : int.Parse(link[(link.IndexOf("page=", StringComparison.Ordinal) + 5)..], System.Globalization.CultureInfo.InvariantCulture);
            var page = Files.Skip(start).Take(2).Select(f => new OneDriveChange(f.Id, false, f)).ToList();
            var more = start + 2 < Files.Count;
            if (more)
            {
                return Task.FromResult(new OneDriveDeltaPage(page, $"https://graph.microsoft.com/v1.0/me/drive/root/delta?page={start + 2}", null));
            }

            DeltaLink = "https://graph.microsoft.com/v1.0/me/drive/root/delta?token=d1";
            return Task.FromResult(new OneDriveDeltaPage(page, null, NoDeltaLink ? null : DeltaLink));
        }

        var changes = Changes.ToList();
        Changes.Clear();
        DeltaLink = "https://graph.microsoft.com/v1.0/me/drive/root/delta?token=d2";
        return Task.FromResult(new OneDriveDeltaPage(changes, null, NoDeltaLink ? null : DeltaLink));
    }

    public Task<OneDriveFolder?> GetFolderAsync(string accessToken, string itemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Folders.GetValueOrDefault(itemId));

    public Task DownloadAsync(string accessToken, string itemId, Stream destination, CancellationToken cancellationToken = default)
    {
        if (itemId == FailDownloadOf)
        {
            throw new HttpRequestException("the download failed");
        }

        return destination.WriteAsync(Content[itemId], cancellationToken).AsTask();
    }
}
