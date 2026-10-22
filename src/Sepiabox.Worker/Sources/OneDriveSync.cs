using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Storage;
using Sepiabox.Worker.Cloud;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// <c>sepiabox-worker sync onedrive</c> (S-039, D-116): for each connected OneDrive, puts the photos and videos that are new or changed in
/// <c>incoming</c> in reference mode (D-023) and marks the sources of those that are gone, exactly as the Google Drive sync does
/// (<see cref="DriveSync"/>, D-115), with Microsoft Graph's delta in place of Google's listing and changes. The first delta enumerates the
/// whole OneDrive, page by page, and ends with a delta link; the sync keeps the next link of the page it stopped at (a few files a run,
/// <paramref name="maxFilesPerRun"/>) and then the delta link, so every run, the first or a later one, is the same loop. A refresh token that
/// Microsoft rolled is stored (re-protected) as soon as it is received.
/// </summary>
public sealed class OneDriveSync(
    CatalogDbContext db,
    IBlobStore store,
    IOneDriveSyncClient client,
    IDataProtectionProvider protection,
    TimeProvider clock,
    ILogger logger,
    int maxFilesPerRun = DriveSync.DefaultMaxFilesPerRun)
{
    private const int MaxFolderDepth = 50;

    private readonly IDataProtector _tokens = protection.CreateProtector(ConnectedSource.RefreshTokenPurpose);
    private readonly IncomingStager _stager = new(store);
    private readonly SourceMarks _marks = new(db, clock);

    public async Task<DriveSyncResult> RunAsync(CancellationToken cancellationToken)
    {
        var totals = new Totals();
        var accounts = await db.ConnectedSources
            .Where(s => s.Type == ConnectedSourceTypes.OneDrive && s.Status == ConnectedSourceStatuses.Connected)
            .OrderBy(s => s.Id).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            await SyncAccountAsync(account, totals, cancellationToken);
        }

        return new DriveSyncResult(accounts.Count, totals.Staged, totals.Removed, totals.Skipped, totals.Reconnect, totals.Failed);
    }

    /// <summary>Forgets where the sync was in an account and what it staged (<c>--rescan</c>); see <see cref="CloudRescan"/>.</summary>
    public Task<bool> RescanAsync(string accountEmail, CancellationToken cancellationToken) =>
        CloudRescan.RescanAsync(db, ConnectedSourceTypes.OneDrive, accountEmail, cancellationToken);

    private async Task SyncAccountAsync(ConnectedSource account, Totals totals, CancellationToken cancellationToken)
    {
        string refreshToken;
        try
        {
            refreshToken = Encoding.UTF8.GetString(_tokens.Unprotect(WebEncoders.Base64UrlDecode(account.EncryptedRefreshToken)));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            // The key is not in the ring, the purpose changed or the value is damaged: the family connects the account again.
            Log.DriveTokenUnreadable(logger, account.Id);
            await MarkReconnectAsync(account, totals, cancellationToken);
            return;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The key ring could not be read just now: nothing is wrong with the account, so it is tried again next run.
            Log.DriveAccountStopped(logger, account.Id, ex.GetType().Name);
            totals.Failed++;
            return;
        }

        try
        {
            var token = await client.RefreshAsync(refreshToken, cancellationToken);
            if (token.RefreshToken is { } rolled && rolled != refreshToken)
            {
                // Kept before anything else: the old one stops being the one that keeps the account alive.
                account.EncryptedRefreshToken = _tokens.Protect(rolled);
                await db.SaveChangesAsync(cancellationToken);
            }

            var run = new AccountRun(account, token.AccessToken, maxFilesPerRun);
            await DeltaAsync(run, totals, cancellationToken);
            account.LastSyncAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            Log.DriveAccountSynced(logger, account.Id, run.Staged, account.SyncChangesToken is null);
        }
        catch (CloudAuthorizationException)
        {
            await MarkReconnectAsync(account, totals, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or DeltaResyncRequiredException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested) || store.IsStoreFailure(ex))
        {
            // The page the run stopped on is asked for again next time: its link was not kept, and what it staged is known.
            Log.DriveAccountStopped(logger, account.Id, ex.GetType().Name);
            totals.Failed++;
        }
    }

    private async Task DeltaAsync(AccountRun run, Totals totals, CancellationToken cancellationToken)
    {
        var account = run.Account;
        while (run.Remaining > 0)
        {
            var link = account.SyncListToken ?? account.SyncChangesToken;
            OneDriveDeltaPage page;
            try
            {
                page = await client.DeltaAsync(run.Token, link, cancellationToken);
            }
            catch (DeltaResyncRequiredException) when (link is not null && !run.Resynced)
            {
                // The link is too old: list the OneDrive again from the start. What was staged is known (drive_files), so only what
                // changed since is staged again.
                run.Resynced = true;
                (account.SyncChangesToken, account.SyncListToken, account.SyncListing) = (null, null, false);
                await db.SaveChangesAsync(cancellationToken);
                Log.OneDriveResync(logger, account.Id);
                continue;
            }

            foreach (var change in page.Changes)
            {
                if (change is { Removed: false, File: { } file })
                {
                    await StageAsync(run, file, totals, cancellationToken);
                }
                else
                {
                    totals.Removed += await RemoveAsync(account, change.Id, cancellationToken);
                }
            }

            if (page.NextLink is { } next)
            {
                account.SyncListToken = next;
                account.SyncListing = account.SyncChangesToken is null;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            // The end of the delta: from here on, the delta link is where to ask from.
            account.SyncChangesToken = page.DeltaLink ?? account.SyncChangesToken;
            (account.SyncListToken, account.SyncListing) = (null, false);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
    }

    private async Task StageAsync(AccountRun run, OneDriveItem file, Totals totals, CancellationToken cancellationToken)
    {
        var account = run.Account;
        var path = await PathAsync(run, file, cancellationToken);
        if (path is null)
        {
            Log.DriveFileSkipped(logger, account.Id, file.Id, "its name is not a photo's or video's");
            totals.Skipped++;
            return;
        }

        var known = await db.DriveFiles.SingleOrDefaultAsync(f => f.ConnectedSourceId == account.Id && f.FileId == file.Id, cancellationToken);
        if (known is not null && known.ModifiedAt == file.ModifiedTime && known.Checksum == file.Hash && known.Path == path)
        {
            await _marks.ClearRemovedAsync(ConnectedSourceTypes.OneDrive, ConnectedSourceTypes.SourceName(ConnectedSourceTypes.OneDrive, account.AccountEmail), path, file.ModifiedTime, file.Size, cancellationToken);
            return;
        }

        if (file.Size is { } size && size > CloudFileProcessor.MaxFileBytes)
        {
            Log.DriveFileSkipped(logger, account.Id, file.Id, "it is larger than the 3 GiB the worker takes");
            totals.Skipped++;
            return;
        }

        var incoming = new IncomingFile(
            ConnectedSourceTypes.OneDrive, ConnectedSourceTypes.SourceName(ConnectedSourceTypes.OneDrive, account.AccountEmail), path, IncomingMode.Reference, file.ModifiedTime, file.Id, IncomingStager.HttpsOnly(file.WebUrl));
        var staged = await _stager.StageAsync(incoming, file.MimeType, (sink, token) => client.DownloadAsync(run.Token, file.Id, sink, token), cancellationToken);
        if (!staged)
        {
            Log.DriveFileSkipped(logger, account.Id, file.Id, "its path is too long for a blob's metadata");
            totals.Skipped++;
            return;
        }

        run.Staged++;
        totals.Staged++;
        if (known is null)
        {
            db.DriveFiles.Add(new DriveFile { ConnectedSourceId = account.Id, FileId = file.Id, Path = path, ModifiedAt = file.ModifiedTime, Checksum = file.Hash, SizeBytes = file.Size });
        }
        else
        {
            if (known.Path != path)
            {
                totals.Removed += await _marks.MarkRemovedAsync(ConnectedSourceTypes.OneDrive, ConnectedSourceTypes.SourceName(ConnectedSourceTypes.OneDrive, account.AccountEmail), known.Path, known.ModifiedAt, known.SizeBytes, cancellationToken);
            }

            (known.Path, known.ModifiedAt, known.Checksum, known.SizeBytes) = (path, file.ModifiedTime, file.Hash, file.Size);
        }

        await _marks.ClearRemovedAsync(ConnectedSourceTypes.OneDrive, ConnectedSourceTypes.SourceName(ConnectedSourceTypes.OneDrive, account.AccountEmail), path, file.ModifiedTime, file.Size, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> RemoveAsync(ConnectedSource account, string itemId, CancellationToken cancellationToken)
    {
        var known = await db.DriveFiles.SingleOrDefaultAsync(f => f.ConnectedSourceId == account.Id && f.FileId == itemId, cancellationToken);
        if (known is null)
        {
            return 0;
        }

        var marked = await _marks.MarkRemovedAsync(ConnectedSourceTypes.OneDrive, ConnectedSourceTypes.SourceName(ConnectedSourceTypes.OneDrive, account.AccountEmail), known.Path, known.ModifiedAt, known.SizeBytes, cancellationToken);
        db.DriveFiles.Remove(known);
        await db.SaveChangesAsync(cancellationToken);
        return marked;
    }

    // A delta gives an item its folder's id and not its path (a renamed folder returns none of what is in it): the names of the folders
    // are asked for by id, once each in a run.
    private async Task<string?> PathAsync(AccountRun run, OneDriveItem file, CancellationToken cancellationToken)
    {
        if (CloudNames.FileName(file.Name, file.MimeType) is not { } name)
        {
            return null;
        }

        var names = new List<string>();
        var parent = file.ParentId;
        for (var depth = 0; parent is not null && depth < MaxFolderDepth; depth++)
        {
            if (!run.Folders.TryGetValue(parent, out var folder))
            {
                folder = await client.GetFolderAsync(run.Token, parent, cancellationToken);
                run.Folders[parent] = folder;
            }

            if (folder is null || folder.ParentId is null)
            {
                // The top of the OneDrive, or a folder that is not there: not part of the path.
                break;
            }

            names.Add(CloudNames.Segment(folder.Name));
            parent = folder.ParentId;
        }

        names.Reverse();
        names.Add(name);
        return ManifestSource.NormalizePath(string.Join('/', names.Where(n => n.Length > 0)));
    }

    private async Task MarkReconnectAsync(ConnectedSource account, Totals totals, CancellationToken cancellationToken)
    {
        account.Status = ConnectedSourceStatuses.Reconnect;
        await db.SaveChangesAsync(cancellationToken);
        Log.DriveNeedsReconnecting(logger, account.Id);
        totals.Reconnect++;
    }

    private sealed class Totals
    {
        public int Staged { get; set; }

        public int Removed { get; set; }

        public int Skipped { get; set; }

        public int Reconnect { get; set; }

        public int Failed { get; set; }
    }

    private sealed class AccountRun(ConnectedSource account, string token, int budget)
    {
        public ConnectedSource Account { get; } = account;

        public string Token { get; } = token;

        public Dictionary<string, OneDriveFolder?> Folders { get; } = [];

        public bool Resynced { get; set; }

        public int Staged { get; set; }

        public int Remaining => budget - Staged;
    }
}
