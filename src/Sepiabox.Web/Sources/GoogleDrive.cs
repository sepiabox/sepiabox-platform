using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Sources;

/// <summary>
/// Google's side of <see cref="IDriveClient"/> and <see cref="IDriveAuthorization"/> (S-037): the Drive API with the
/// same OAuth client as the Google sign-in (<c>Auth:Google</c>), asked for the read-only scope in a flow of its own
/// (incremental authorization). Not run by any test: they use fakes (D-097).
/// </summary>
public sealed class GoogleDrive(OAuthClient? client) : IDriveClient, IDriveAuthorization
{
    private const string UserId = "sepiabox";
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    // Photos and videos that are not in the trash, wherever they are in the Drive.
    private const string MediaQuery = "(mimeType contains 'image/' or mimeType contains 'video/') and trashed = false";

    public bool IsConfigured => client is not null;

    public Uri AuthorizationUri(string redirectUri, string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = (client ?? throw new InvalidOperationException("Auth:Google is not configured.")).ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = DriveService.Scope.DriveReadonly,
            ["access_type"] = "offline",
            // Without it Google sends a refresh token only the first time the account consents.
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state,
        };
        return new Uri(AuthorizationEndpoint + "?" + string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")));
    }

    public async Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await Flow().ExchangeCodeForTokenAsync(UserId, code, redirectUri, cancellationToken);
            return string.IsNullOrEmpty(token.RefreshToken)
                ? throw new DriveAuthorizationException("Google sent no refresh token.")
                : token.RefreshToken;
        }
        catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
        {
            throw new DriveAuthorizationException("Google refused the authorization code.", ex);
        }
    }

    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        Flow().RevokeTokenAsync(UserId, refreshToken, cancellationToken);

    public async Task<string> GetAccountEmailAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var drive = Service(refreshToken);
        var request = drive.About.Get();
        request.Fields = "user(emailAddress)";
        var about = await ExecuteAsync(() => request.ExecuteAsync(cancellationToken));
        return (about.User?.EmailAddress ?? throw new InvalidOperationException("Google did not say whose Drive this is.")).ToLowerInvariant();
    }

    public async Task<DriveMediaCount> CountMediaAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var drive = Service(refreshToken);
        int photos = 0, videos = 0;
        string? page = null;
        do
        {
            var request = drive.Files.List();
            request.Q = MediaQuery;
            request.Fields = "nextPageToken, files(mimeType)";
            request.PageSize = 1000;
            request.PageToken = page;
            var files = await ExecuteAsync(() => request.ExecuteAsync(cancellationToken));
            foreach (var file in files.Files ?? [])
            {
                if (file.MimeType?.StartsWith("video/", StringComparison.Ordinal) == true)
                {
                    videos++;
                }
                else
                {
                    photos++;
                }
            }

            page = files.NextPageToken;
        }
        while (page is not null);

        return new DriveMediaCount(photos, videos);
    }

    private GoogleAuthorizationCodeFlow Flow()
    {
        var oauth = client ?? throw new InvalidOperationException("Auth:Google is not configured.");
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = oauth.ClientId, ClientSecret = oauth.ClientSecret },
            Scopes = [DriveService.Scope.DriveReadonly],
        });
    }

    private DriveService Service(string refreshToken) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = new UserCredential(Flow(), UserId, new TokenResponse { RefreshToken = refreshToken }),
        ApplicationName = "Sepiabox",
    });

    // The credential refreshes the access token on the first call: a token Google no longer honours fails there.
    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
        {
            throw new DriveAuthorizationException("Google no longer honours the token.", ex);
        }
    }
}
