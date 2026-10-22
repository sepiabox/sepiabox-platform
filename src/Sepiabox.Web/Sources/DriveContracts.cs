namespace Sepiabox.Web.Sources;

/// <summary>The photos and videos a Google Drive holds, counted without importing them (S-037).</summary>
public sealed record DriveMediaCount(int Photos, int Videos);

/// <summary>
/// Google refused the token or the code: <c>invalid_grant</c>, which is what a refresh token past the 7 days of an app in
/// Testing mode (D-024), or one revoked, gets. The connection then needs to be made again.
/// </summary>
public sealed class DriveAuthorizationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// What the web asks of an account that is already connected (S-037, D-051): behind an interface so that tests, which
/// never reach Google, use a fake. Every call takes the plain refresh token and throws
/// <see cref="DriveAuthorizationException"/> when Google no longer honours it.
/// </summary>
public interface IDriveClient
{
    /// <summary>The account's address, lower-cased.</summary>
    Task<string> GetAccountEmailAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many photos (<c>image/*</c>) and videos (<c>video/*</c>) the account's Drive has that are not in the trash
    /// (<c>files.list</c>, a page at a time), without reading any of them.
    /// </summary>
    Task<DriveMediaCount> CountMediaAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>The OAuth half (S-037): the address the browser is sent to, the code it comes back with, and revoking.</summary>
public interface IDriveAuthorization
{
    /// <summary>False while the Google OAuth client (<c>Auth:Google</c>) is not configured: nothing can be connected.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Google's consent address for the <c>drive.readonly</c> scope with offline access, always asking for consent (so
    /// that a refresh token comes back), for <paramref name="redirectUri"/> and carrying <paramref name="state"/>.
    /// </summary>
    Uri AuthorizationUri(string redirectUri, string state);

    /// <summary>The refresh token for a code; throws <see cref="DriveAuthorizationException"/> when Google refuses it or sends none.</summary>
    Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    /// <summary>Tells Google to forget the token; callers treat a failure as nothing to do.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
}
