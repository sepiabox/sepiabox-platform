namespace Sepiabox.Web.Sources;

/// <summary>
/// The tokens a refresh gives (S-039). Microsoft's refresh tokens roll: the newest one must be kept, or the account stops working after
/// 90 days, so whoever refreshes stores <see cref="RefreshToken"/> when it is not the one it used.
/// </summary>
/// <param name="AccessToken">For this request's calls to Graph; never stored.</param>
/// <param name="RefreshToken">The refresh token to keep from now on; null when Microsoft sent none (the one used stays).</param>
public sealed record OneDriveWebToken(string AccessToken, string? RefreshToken);

/// <summary>
/// What the web asks of a OneDrive that is already connected (S-039, D-051): behind an interface so that tests, which never reach
/// Microsoft, use a fake. A refresh that Microsoft refuses throws <see cref="Sepiabox.Catalog.CloudAuthorizationException"/>.
/// </summary>
public interface IOneDriveClient
{
    Task<OneDriveWebToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>The account's address, lower-cased.</summary>
    Task<string> GetAccountEmailAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>How many photos and videos the OneDrive has, without reading any of them.</summary>
    Task<DriveMediaCount> CountMediaAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>The content of an item, to pass on to the browser ("Open original"); null when it is gone.</summary>
    Task<DriveFileDownload?> OpenFileAsync(string accessToken, string itemId, CancellationToken cancellationToken = default);
}

/// <summary>The OAuth half of connecting a OneDrive (S-039): the address the browser is sent to, and the code it comes back with.</summary>
public interface IOneDriveAuthorization
{
    /// <summary>False while the OneDrive OAuth client (<c>OneDrive:ClientId</c>, <c>OneDrive:ClientSecret</c>) is not configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Microsoft's consent address for <c>Files.Read offline_access User.Read</c>, for <paramref name="redirectUri"/> and carrying <paramref name="state"/>.</summary>
    Uri AuthorizationUri(string redirectUri, string state);

    /// <summary>The refresh token for a code; throws <see cref="Sepiabox.Catalog.CloudAuthorizationException"/> when Microsoft refuses it or sends none.</summary>
    Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);
}
