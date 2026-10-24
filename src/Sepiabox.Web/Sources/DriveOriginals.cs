using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;

namespace Sepiabox.Web.Sources;

/// <summary>What opening the original of a Drive source came to.</summary>
public enum DriveOriginalOutcome
{
    Opened,

    /// <summary>No such source, not a Drive one, not of this asset, or the file is gone from the Drive.</summary>
    NotFound,

    /// <summary>Google no longer honours the account's token: the status is now <c>reconnect</c>.</summary>
    Reconnect,

    /// <summary>Anything else that went wrong (Google unreachable).</summary>
    Failed,
}

/// <summary>"Open original" for a Google Drive or OneDrive source (S-038, S-039): the file, fetched from the Drive through the server.</summary>
public interface IDriveOriginals
{
    Task<(DriveOriginalOutcome Outcome, DriveFileDownload? File)> OpenAsync(string assetId, long sourceId, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IDriveOriginals"/> on the catalog (the source, the account, the file's id the sync recorded) and the account's token.</summary>
public sealed partial class DriveOriginals(
    IDbContextFactory<CatalogDbContext> contexts, DriveTokenProtector tokens, IDriveClient drive, IOneDriveClient oneDrive, ILogger<DriveOriginals> log) : IDriveOriginals
{
    public async Task<(DriveOriginalOutcome Outcome, DriveFileDownload? File)> OpenAsync(string assetId, long sourceId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var source = await db.AssetSources.AsNoTracking().SingleOrDefaultAsync(
            s => s.Id == sourceId && s.AssetId == assetId && (s.Type == ConnectedSourceTypes.GoogleDrive || s.Type == ConnectedSourceTypes.OneDrive), cancellationToken);
        if (source is null)
        {
            return (DriveOriginalOutcome.NotFound, null);
        }

        // The account the source was found in, and the id the sync gave the file: the manifest only has the path.
        var email = ConnectedSourceTypes.AccountOf(source.Type, source.Name);
        var account = email is null ? null : await db.ConnectedSources.SingleOrDefaultAsync(
            s => s.Type == source.Type && s.AccountEmail == email, cancellationToken);
        var file = account is null || source.RemovedAt is not null ? null : await FileOfAsync(db, account.Id, source, assetId, cancellationToken);
        if (account is null || file is null)
        {
            return (DriveOriginalOutcome.NotFound, null);
        }

        if (tokens.Unprotect(account.EncryptedRefreshToken) is not { } token)
        {
            return (await NeedReconnectingAsync(db, account, cancellationToken), null);
        }

        try
        {
            DriveFileDownload? download;
            if (account.Type == ConnectedSourceTypes.OneDrive)
            {
                var access = await oneDrive.RefreshAsync(token, cancellationToken);
                if (access.RefreshToken is { Length: > 0 } rolled && rolled != token)
                {
                    // Microsoft rolls the refresh token: the new one is kept at once, or the account stops working.
                    account.EncryptedRefreshToken = tokens.Protect(rolled);
                    await db.SaveChangesAsync(cancellationToken);
                }

                download = await oneDrive.OpenFileAsync(access.AccessToken, file.FileId, cancellationToken);
            }
            else
            {
                download = await drive.OpenFileAsync(token, file.FileId, cancellationToken);
            }

            return download is { } opened
                ? (DriveOriginalOutcome.Opened, opened)
                : (DriveOriginalOutcome.NotFound, null);
        }
        catch (CloudAuthorizationException)
        {
            return (await NeedReconnectingAsync(db, account, cancellationToken), null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogOpenFailed(log, account.Id, ex);
            return (DriveOriginalOutcome.Failed, null);
        }
    }

    // A Drive can hold two files of one name in one folder, so the path alone may name several. The one the source describes has the
    // source's modification time and the asset's size; when the time is not the same yet (a change the import has not reached), the one
    // file at that path is it. Anything still ambiguous is not found, never a guess and never an error.
    private static async Task<DriveFile?> FileOfAsync(CatalogDbContext db, long accountId, AssetSource source, string assetId, CancellationToken cancellationToken)
    {
        var atPath = await db.DriveFiles.AsNoTracking().Where(f => f.ConnectedSourceId == accountId && f.Path == source.Path).ToListAsync(cancellationToken);
        var size = await db.Assets.AsNoTracking().Where(a => a.Id == assetId).Select(a => (long?)a.SizeBytes).SingleOrDefaultAsync(cancellationToken);
        var same = atPath.Where(f => f.ModifiedAt == source.ModifiedAt && (f.SizeBytes is null || f.SizeBytes == size)).ToList();
        return same.Count == 1 ? same[0] : same.Count == 0 && atPath.Count == 1 ? atPath[0] : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening a file of the cloud account of connection {Id} failed.")]
    private static partial void LogOpenFailed(ILogger logger, long id, Exception exception);

    private static async Task<DriveOriginalOutcome> NeedReconnectingAsync(CatalogDbContext db, ConnectedSource account, CancellationToken cancellationToken)
    {
        account.Status = ConnectedSourceStatuses.Reconnect;
        await db.SaveChangesAsync(cancellationToken);
        return DriveOriginalOutcome.Reconnect;
    }
}
