using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Storage;
using Sepiabox.Worker.Cloud;

namespace Sepiabox.Worker.Sources;

/// <summary>What one run of the sync did, for the exit code and the log.</summary>
/// <param name="Accounts">The connected accounts it went through.</param>
/// <param name="Staged">The files it put in <c>incoming</c>.</param>
/// <param name="Removed">The sources it marked as removed.</param>
/// <param name="Skipped">The files it left alone for a reason (too large, a name that is not a photo's, metadata too long); each logged.</param>
/// <param name="Reconnect">The accounts whose token Google no longer honours, or that could not be read.</param>
/// <param name="Failed">The accounts it stopped on because a file could not be fetched or stored; the next run goes on from there.</param>
public sealed record DriveSyncResult(int Accounts, int Staged, int Removed, int Skipped, int Reconnect, int Failed);

/// <summary>
/// <c>sepiabox-worker sync google-drive</c> (S-038, D-115): for each connected Google Drive, puts the photos and videos that are new or
/// changed in <c>incoming</c> in reference mode (D-023), and marks the sources of those that are gone. The first run lists everything, a
/// few files a run (<paramref name="maxFilesPerRun"/>, so a first sync of a large Drive is spread over days and the processing job and
/// its cost keep up); the next ones read the changes from the token the last run kept. It follows the incoming contract (D-107): a new
/// blob name for each transfer, the metadata in the commit of the staged upload, within 8 KB, and an <c>https</c> address only.
/// </summary>
/// <param name="db">The catalog, as the application role.</param>
/// <param name="store">The blobs (D-051).</param>
/// <param name="client">Google's side.</param>
/// <param name="protection">The key ring the web protected the refresh tokens with.</param>
/// <param name="clock">Dates the sync and the removals.</param>
/// <param name="logger">Reports what it did. Never a token, a file's name or an address: only ids and counts.</param>
/// <param name="maxFilesPerRun">The most files one run stages for an account.</param>
public sealed class DriveSync(
    CatalogDbContext db,
    IBlobStore store,
    IDriveSyncClient client,
    IDataProtectionProvider protection,
    TimeProvider clock,
    ILogger logger,
    int maxFilesPerRun = DriveSync.DefaultMaxFilesPerRun)
{
    /// <summary>The default number of files staged for an account in a run.</summary>
    public const int DefaultMaxFilesPerRun = 500;

    /// <summary>The most a blob's metadata may be, names and values together (Azure's limit; D-107).</summary>
    public const int MaxMetadataBytes = 8 * 1024;

    private const int MaxFolderDepth = 50;

    private readonly IDataProtector _tokens = protection.CreateProtector(ConnectedSource.RefreshTokenPurpose);

    public async Task<DriveSyncResult> RunAsync(CancellationToken cancellationToken)
    {
        var totals = new Totals();
        var accounts = await db.ConnectedSources
            .Where(s => s.Type == ConnectedSourceTypes.GoogleDrive && s.Status == ConnectedSourceStatuses.Connected)
            .OrderBy(s => s.Id).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            await SyncAccountAsync(account, totals, cancellationToken);
        }

        return new DriveSyncResult(accounts.Count, totals.Staged, totals.Removed, totals.Skipped, totals.Reconnect, totals.Failed);
    }

    private async Task SyncAccountAsync(ConnectedSource account, Totals totals, CancellationToken cancellationToken)
    {
        var token = Unprotect(account.EncryptedRefreshToken);
        if (token is null)
        {
            // Another key ring, a changed purpose or a damaged value: the family connects the account again.
            Log.DriveTokenUnreadable(logger, account.Id);
            await MarkReconnectAsync(account, totals, cancellationToken);
            return;
        }

        var run = new AccountRun(account, token, maxFilesPerRun);
        try
        {
            if (account.SyncChangesToken is null)
            {
                // Before the listing, so what changes while it goes on is seen by the changes that follow.
                account.SyncChangesToken = await client.GetStartPageTokenAsync(token, cancellationToken);
                (account.SyncListing, account.SyncListToken) = (true, null);
                await db.SaveChangesAsync(cancellationToken);
            }

            if (account.SyncListing)
            {
                await ListAsync(run, totals, cancellationToken);
            }
            else
            {
                await ChangesAsync(run, totals, cancellationToken);
            }

            account.LastSyncAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            Log.DriveAccountSynced(logger, account.Id, run.Staged, account.SyncListing);
        }
        catch (DriveAuthorizationException)
        {
            await MarkReconnectAsync(account, totals, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || store.IsStoreFailure(ex))
        {
            // The page the run stopped on is tried again next time: its cursor was not moved, and what it staged is known.
            Log.DriveAccountStopped(logger, account.Id, ex.GetType().Name);
            totals.Failed++;
        }
    }

    private async Task ListAsync(AccountRun run, Totals totals, CancellationToken cancellationToken)
    {
        var account = run.Account;
        while (run.Remaining > 0)
        {
            var page = await client.ListMediaAsync(run.Token, account.SyncListToken, cancellationToken);
            foreach (var file in page.Files)
            {
                await StageAsync(run, file, totals, cancellationToken);
            }

            account.SyncListToken = page.NextPageToken;
            account.SyncListing = page.NextPageToken is not null;
            await db.SaveChangesAsync(cancellationToken);
            if (!account.SyncListing)
            {
                return;
            }
        }
    }

    private async Task ChangesAsync(AccountRun run, Totals totals, CancellationToken cancellationToken)
    {
        var account = run.Account;
        while (run.Remaining > 0)
        {
            var page = await client.ListChangesAsync(run.Token, account.SyncChangesToken!, cancellationToken);
            foreach (var change in page.Changes)
            {
                if (change is { Removed: false, File: { } file })
                {
                    await StageAsync(run, file, totals, cancellationToken);
                }
                else
                {
                    totals.Removed += await RemoveAsync(account, change.FileId, cancellationToken);
                }
            }

            var next = page.NextPageToken ?? page.NewStartPageToken;
            if (next is not null)
            {
                account.SyncChangesToken = next;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (page.NextPageToken is null)
            {
                return;
            }
        }
    }

    private async Task StageAsync(AccountRun run, DriveFileInfo file, Totals totals, CancellationToken cancellationToken)
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
        if (known is not null && known.ModifiedAt == file.ModifiedTime && known.Checksum == file.Md5 && known.Path == path)
        {
            // Not changed since it was staged (a listing run twice, a change that is only a view or a share): the source is not removed.
            await ClearRemovedAsync(account, path, cancellationToken);
            return;
        }

        if (file.Size is { } size && size > CloudFileProcessor.MaxFileBytes)
        {
            Log.DriveFileSkipped(logger, account.Id, file.Id, "it is larger than the 3 GiB the worker takes");
            totals.Skipped++;
            return;
        }

        var incoming = new IncomingFile(
            ConnectedSourceTypes.GoogleDrive, account.AccountEmail, path, IncomingMode.Reference, file.ModifiedTime, file.Id, HttpsOnly(file.WebViewLink));
        var metadata = incoming.ToMetadata();
        if (MetadataBytes(metadata) > MaxMetadataBytes)
        {
            // Never truncated: a path cut short would be another file's (D-107).
            Log.DriveFileSkipped(logger, account.Id, file.Id, "its path is too long for a blob's metadata");
            totals.Skipped++;
            return;
        }

        await TransferAsync(run, file, metadata, cancellationToken);
        run.Staged++;
        totals.Staged++;

        if (known is null)
        {
            db.DriveFiles.Add(new DriveFile { ConnectedSourceId = account.Id, FileId = file.Id, Path = path, ModifiedAt = file.ModifiedTime, Checksum = file.Md5 });
        }
        else
        {
            if (known.Path != path)
            {
                // Moved or renamed: the file is at its new path now, and no longer at the old one.
                totals.Removed += await MarkRemovedAsync(account, known.Path, cancellationToken);
            }

            (known.Path, known.ModifiedAt, known.Checksum) = (path, file.ModifiedTime, file.Md5);
        }

        await ClearRemovedAsync(account, path, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    // A new blob name for each transfer (D-107): the name means one set of bytes, and the processing job deletes it when it is done.
    private async Task TransferAsync(AccountRun run, DriveFileInfo file, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        var name = Guid.NewGuid().ToString("N");
        await using var upload = store.BeginUpload(BlobLayout.IncomingContainer, name);
        await using var sink = new StagedBlockStream(upload);
        await client.DownloadAsync(run.Token, file.Id, sink, cancellationToken);
        await sink.FinishAsync(cancellationToken);
        // The commit makes the blob and gives it its metadata in one call, so the event never finds it bare (D-107).
        await upload.CommitAsync(file.MimeType, BlobStorageClass.Default, metadata, cancellationToken);
    }

    private async Task<int> RemoveAsync(ConnectedSource account, string fileId, CancellationToken cancellationToken)
    {
        var known = await db.DriveFiles.SingleOrDefaultAsync(f => f.ConnectedSourceId == account.Id && f.FileId == fileId, cancellationToken);
        if (known is null)
        {
            return 0;
        }

        var marked = await MarkRemovedAsync(account, known.Path, cancellationToken);
        db.DriveFiles.Remove(known);
        await db.SaveChangesAsync(cancellationToken);
        return marked;
    }

    private Task<int> MarkRemovedAsync(ConnectedSource account, string path, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return db.AssetSources
            .Where(s => s.Type == ConnectedSourceTypes.GoogleDrive && s.Name == account.AccountEmail && s.Path == path && s.RemovedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RemovedAt, now), cancellationToken);
    }

    private Task<int> ClearRemovedAsync(ConnectedSource account, string path, CancellationToken cancellationToken) =>
        db.AssetSources
            .Where(s => s.Type == ConnectedSourceTypes.GoogleDrive && s.Name == account.AccountEmail && s.Path == path && s.RemovedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RemovedAt, (DateTimeOffset?)null), cancellationToken);

    // The folders' names and the file's, with '/', and an extension if the name has none; null when it is not a photo or video.
    private async Task<string?> PathAsync(AccountRun run, DriveFileInfo file, CancellationToken cancellationToken)
    {
        var name = file.Name.Replace('/', '-').Replace('\\', '-').Trim();
        if (MediaTypes.Classify(name) == MediaKind.Other)
        {
            name += MimeExtension(file.MimeType);
        }

        if (name.Length == 0 || MediaTypes.Classify(name) == MediaKind.Other)
        {
            return null;
        }

        var names = new List<string>();
        var parent = file.Parents is [var first, ..] ? first : null;
        for (var depth = 0; parent is not null && depth < MaxFolderDepth; depth++)
        {
            if (!run.Folders.TryGetValue(parent, out var folder))
            {
                folder = await client.GetFolderAsync(run.Token, parent, cancellationToken);
                run.Folders[parent] = folder;
            }

            if (folder is null || folder.ParentId is null)
            {
                // The top of the Drive, or a folder the account cannot see: it is not part of the path.
                break;
            }

            names.Add(folder.Name.Replace('/', '-').Replace('\\', '-').Trim());
            parent = folder.ParentId;
        }

        names.Reverse();
        names.Add(name);
        return ManifestSource.NormalizePath(string.Join('/', names.Where(n => n.Length > 0)));
    }

    private static string MimeExtension(string mimeType) => mimeType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/heic" => ".heic",
        "image/heif" => ".heif",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/tiff" => ".tif",
        "video/mp4" => ".mp4",
        "video/quicktime" => ".mov",
        "video/x-msvideo" => ".avi",
        "video/x-matroska" => ".mkv",
        "video/mpeg" => ".mpg",
        _ => string.Empty,
    };

    // Only an absolute https address without credentials, within the manifest's limit: the schema refuses any other (D-107).
    private static string? HttpsOnly(string? url) =>
        url is { Length: > 0 and <= ManifestSource.MaxUrlLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
            ? url
            : null;

    private static int MetadataBytes(IReadOnlyDictionary<string, string> metadata) =>
        metadata.Sum(p => Encoding.UTF8.GetByteCount(p.Key) + Encoding.UTF8.GetByteCount(p.Value));

    private string? Unprotect(string value)
    {
        try
        {
            return _tokens.Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
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

        public Dictionary<string, DriveFolder?> Folders { get; } = [];

        public int Staged { get; set; }

        public int Remaining => budget - Staged;
    }
}
