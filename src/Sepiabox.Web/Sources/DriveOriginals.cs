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

/// <summary>"Open original" for a Google Drive source (S-038): the file, fetched from the Drive through the server.</summary>
public interface IDriveOriginals
{
    Task<(DriveOriginalOutcome Outcome, DriveFileDownload? File)> OpenAsync(string assetId, long sourceId, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IDriveOriginals"/> on the catalog (the source, the account, the file's id the sync recorded) and the account's token.</summary>
public sealed partial class DriveOriginals(
    IDbContextFactory<CatalogDbContext> contexts, DriveTokenProtector tokens, IDriveClient drive, ILogger<DriveOriginals> log) : IDriveOriginals
{
    public async Task<(DriveOriginalOutcome Outcome, DriveFileDownload? File)> OpenAsync(string assetId, long sourceId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var source = await db.AssetSources.AsNoTracking().SingleOrDefaultAsync(
            s => s.Id == sourceId && s.AssetId == assetId && s.Type == ConnectedSourceTypes.GoogleDrive, cancellationToken);
        if (source is null)
        {
            return (DriveOriginalOutcome.NotFound, null);
        }

        // The account the source was found in, and the id the sync gave the file: the manifest only has the path.
        var account = await db.ConnectedSources.SingleOrDefaultAsync(
            s => s.Type == ConnectedSourceTypes.GoogleDrive && s.AccountEmail == source.Name, cancellationToken);
        var file = account is null
            ? null
            : await db.DriveFiles.AsNoTracking().SingleOrDefaultAsync(f => f.ConnectedSourceId == account.Id && f.Path == source.Path, cancellationToken);
        if (account is null || file is null || source.RemovedAt is not null)
        {
            return (DriveOriginalOutcome.NotFound, null);
        }

        if (tokens.Unprotect(account.EncryptedRefreshToken) is not { } token)
        {
            return (await NeedReconnectingAsync(db, account, cancellationToken), null);
        }

        try
        {
            return await drive.OpenFileAsync(token, file.FileId, cancellationToken) is { } opened
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening a file of the Drive of connection {Id} failed.")]
    private static partial void LogOpenFailed(ILogger logger, long id, Exception exception);

    private static async Task<DriveOriginalOutcome> NeedReconnectingAsync(CatalogDbContext db, ConnectedSource account, CancellationToken cancellationToken)
    {
        account.Status = ConnectedSourceStatuses.Reconnect;
        await db.SaveChangesAsync(cancellationToken);
        return DriveOriginalOutcome.Reconnect;
    }
}
