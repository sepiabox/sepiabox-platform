using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;

namespace Sepiabox.Web.Sources;

/// <summary>One connection on the sources page (S-037): never the token, protected or not.</summary>
public sealed record ConnectedSourceSummary(
    long Id,
    string Type,
    string AccountEmail,
    string Status,
    DateTimeOffset ConnectedAt,
    DateTimeOffset? LastSyncAt,
    int? PhotoCount,
    int? VideoCount,
    DateTimeOffset? CountedAt)
{
    public bool NeedsReconnecting => Status == ConnectedSourceStatuses.Reconnect;
}

/// <summary>What a request on a connection came to.</summary>
public enum SourceOutcome
{
    Done,
    NotFound,

    /// <summary>Google no longer honours the token (<c>invalid_grant</c>): the status is now <c>reconnect</c>.</summary>
    Reconnect,

    /// <summary>Anything else that went wrong (Google unreachable, a refused code); nothing was changed.</summary>
    Failed,
}

/// <summary>The connected cloud accounts (S-037, D-097), one short-lived context each.</summary>
public interface IConnectedSources
{
    /// <summary>Whether the Google OAuth client is configured, so "Connect Google Drive" can work.</summary>
    bool CanConnectGoogleDrive { get; }

    Task<IReadOnlyList<ConnectedSourceSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Google's consent address for a connection (see <see cref="IDriveAuthorization.AuthorizationUri"/>).</summary>
    Uri GoogleDriveAuthorizationUri(string redirectUri, string state);

    /// <summary>
    /// Finishes a connection with the code Google sent back: exchanges it, asks whose Drive it is, and stores the token
    /// protected, as a new connection or, for an account already connected, in place of its old token (status
    /// <c>connected</c> again).
    /// </summary>
    Task<SourceOutcome> CompleteGoogleDriveConnectionAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the account's photos and videos without importing them and keeps the numbers; a token Google refuses (or
    /// that cannot be read any more) makes the status <c>reconnect</c>.
    /// </summary>
    Task<SourceOutcome> CountAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tells Google to forget the token, as far as it can, and deletes the connection and its token.</summary>
    Task<SourceOutcome> DisconnectAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Protects a refresh token for the catalog (S-037): only <see cref="IDataProtector"/>, whose keys are in Blob Storage
/// and protected by a Key Vault key in Azure (D-051, S-032), so the database alone never holds a token in clear text.
/// </summary>
public sealed class DriveTokenProtector(IDataProtectionProvider provider)
{
    /// <summary>The purpose, so no other protector of the app can read or forge a token.</summary>
    public const string Purpose = ConnectedSource.RefreshTokenPurpose;

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return _protector.Protect(token);
    }

    /// <summary>The token, or null when it cannot be read: another key ring, a changed purpose, a damaged value.</summary>
    public string? Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary><see cref="IConnectedSources"/> on the catalog, Google's Drive and the app's data protection.</summary>
public sealed partial class ConnectedSources(
    IDbContextFactory<CatalogDbContext> contexts,
    DriveTokenProtector tokens,
    IDriveClient drive,
    IDriveAuthorization authorization,
    TimeProvider clock,
    ILogger<ConnectedSources> log) : IConnectedSources
{
    public bool CanConnectGoogleDrive => authorization.IsConfigured;

    public async Task<IReadOnlyList<ConnectedSourceSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var sources = await db.ConnectedSources.AsNoTracking().OrderBy(s => s.Type).ThenBy(s => s.AccountEmail).ToListAsync(cancellationToken);
        return [.. sources.Select(s => new ConnectedSourceSummary(
            s.Id, s.Type, s.AccountEmail, s.Status, s.ConnectedAt, s.LastSyncAt, s.PhotoCount, s.VideoCount, s.CountedAt))];
    }

    public Uri GoogleDriveAuthorizationUri(string redirectUri, string state) => authorization.AuthorizationUri(redirectUri, state);

    public async Task<SourceOutcome> CompleteGoogleDriveConnectionAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        string token, email;
        try
        {
            token = await authorization.ExchangeCodeAsync(code, redirectUri, cancellationToken);
            email = (await drive.GetAccountEmailAsync(token, cancellationToken)).Trim().ToLowerInvariant();
        }
        catch (CloudAuthorizationException ex)
        {
            LogCodeRefused(log, ex);
            return SourceOutcome.Failed;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogConnectFailed(log, ex);
            return SourceOutcome.Failed;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var protectedToken = tokens.Protect(token);
        var existing = await FindAsync(db, email, cancellationToken);
        if (existing is null)
        {
            db.ConnectedSources.Add(new ConnectedSource
            {
                Type = ConnectedSourceTypes.GoogleDrive,
                AccountEmail = email,
                EncryptedRefreshToken = protectedToken,
                Status = ConnectedSourceStatuses.Connected,
                ConnectedAt = clock.GetUtcNow(),
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return SourceOutcome.Done;
            }
            catch (DbUpdateException)
            {
                // Two callbacks for a new account at once: the other one won the unique key, so this is an update.
                db.ChangeTracker.Clear();
                existing = await FindAsync(db, email, cancellationToken) ?? throw new InvalidOperationException("The connection could not be saved.");
            }
        }

        // The same account connecting again: a new token, and the counts and the last sync stay.
        existing.EncryptedRefreshToken = protectedToken;
        existing.Status = ConnectedSourceStatuses.Connected;
        await db.SaveChangesAsync(cancellationToken);
        return SourceOutcome.Done;
    }

    private static Task<ConnectedSource?> FindAsync(CatalogDbContext db, string email, CancellationToken cancellationToken) =>
        db.ConnectedSources.SingleOrDefaultAsync(s => s.Type == ConnectedSourceTypes.GoogleDrive && s.AccountEmail == email, cancellationToken);

    public async Task<SourceOutcome> CountAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var source = await db.ConnectedSources.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null)
        {
            return SourceOutcome.NotFound;
        }

        // A token that cannot be read (the key ring was replaced) is as good as one Google refuses: connect again.
        if (tokens.Unprotect(source.EncryptedRefreshToken) is not { } token)
        {
            return await NeedReconnectingAsync(db, source, cancellationToken);
        }

        DriveMediaCount found;
        try
        {
            found = await drive.CountMediaAsync(token, cancellationToken);
        }
        catch (CloudAuthorizationException ex)
        {
            LogTokenRefused(log, id, ex);
            return await NeedReconnectingAsync(db, source, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogCountFailed(log, id, ex);
            return SourceOutcome.Failed;
        }

        (source.PhotoCount, source.VideoCount, source.CountedAt, source.Status) =
            (found.Photos, found.Videos, clock.GetUtcNow(), ConnectedSourceStatuses.Connected);
        await db.SaveChangesAsync(cancellationToken);
        return SourceOutcome.Done;
    }

    public async Task<SourceOutcome> DisconnectAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var source = await db.ConnectedSources.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null)
        {
            return SourceOutcome.NotFound;
        }

        // Google is asked to forget the token, but a failure never keeps the family from deleting it here.
        if (tokens.Unprotect(source.EncryptedRefreshToken) is { } token)
        {
            try
            {
                await authorization.RevokeAsync(token, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogRevokeFailed(log, id, ex);
            }
        }

        db.ConnectedSources.Remove(source);
        await db.SaveChangesAsync(cancellationToken);
        return SourceOutcome.Done;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google refused the Drive authorization code.")]
    private static partial void LogCodeRefused(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connecting a Google Drive failed.")]
    private static partial void LogConnectFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Google no longer honours the Drive token of connection {Id}.")]
    private static partial void LogTokenRefused(ILogger logger, long id, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Counting the Drive of connection {Id} failed.")]
    private static partial void LogCountFailed(ILogger logger, long id, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Google did not revoke the Drive token of connection {Id}.")]
    private static partial void LogRevokeFailed(ILogger logger, long id, Exception exception);

    private static async Task<SourceOutcome> NeedReconnectingAsync(CatalogDbContext db, ConnectedSource source, CancellationToken cancellationToken)
    {
        source.Status = ConnectedSourceStatuses.Reconnect;
        await db.SaveChangesAsync(cancellationToken);
        return SourceOutcome.Reconnect;
    }
}
