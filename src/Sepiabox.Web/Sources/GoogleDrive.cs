using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Sepiabox.Catalog;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Sources;

/// <summary>
/// Google's side of <see cref="IDriveClient"/> and <see cref="IDriveAuthorization"/> (S-037): the Drive API with the
/// same OAuth client as the Google sign-in (<c>Auth:Google</c>), asked for the read-only scope in a flow of its own
/// (incremental authorization). The tests of the web use fakes of the interfaces; this class is tested alone with a
/// fake <see cref="HttpMessageHandler"/> in place of Google (D-097).
/// </summary>
public sealed class GoogleDrive(OAuthClient? client, HttpMessageHandler? handler = null) : IDriveClient, IDriveAuthorization
{
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

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
                ? throw new CloudAuthorizationException("Google sent no refresh token.")
                : token.RefreshToken;
        }
        catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
        {
            throw new CloudAuthorizationException("Google refused the authorization code.", ex);
        }
    }

    /// <summary>
    /// The token goes in the body of a form, never in the address: the HTTP instrumentation records whole URLs, and
    /// Google's own client sends it as <c>?token=</c>, which would put a live refresh token in the telemetry. A failure
    /// is thrown without the request or the token in it.
    /// </summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(new SharedHandler(handler));
        using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken });
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(new Uri(RevokeEndpoint), body, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Google could not be reached to revoke the token.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Google answered {(int)response.StatusCode} to the revocation.");
            }
        }
    }

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

    public async Task<DriveFileDownload?> OpenFileAsync(string refreshToken, string fileId, CancellationToken cancellationToken = default)
    {
        var drive = Service(refreshToken);
        try
        {
            var info = drive.Files.Get(fileId);
            info.Fields = "name,mimeType,size,trashed";
            var file = await ExecuteAsync(() => info.ExecuteAsync(cancellationToken));
            if (file.Trashed == true)
            {
                drive.Dispose();
                return null;
            }

            var media = drive.Files.Get(fileId);
            // The service stays alive with the stream: disposing the client would cut the content short.
            var content = await ExecuteAsync(() => media.ExecuteAsStreamAsync(cancellationToken));
            return new DriveFileDownload(content, file.Name ?? "original", file.MimeType ?? "application/octet-stream", file.Size, drive);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            drive.Dispose();
            return null;
        }
        catch
        {
            drive.Dispose();
            throw;
        }
    }

    private GoogleAuthorizationCodeFlow Flow()
    {
        var oauth = client ?? throw new InvalidOperationException("Auth:Google is not configured.");
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = oauth.ClientId, ClientSecret = oauth.ClientSecret },
            Scopes = [DriveService.Scope.DriveReadonly],
            HttpClientFactory = handler is null ? new HttpClientFactory() : new HandlerFactory(handler),
        });
    }

    private DriveService Service(string refreshToken) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = new UserCredential(Flow(), UserId, new TokenResponse { RefreshToken = refreshToken }),
        ApplicationName = "Sepiabox",
        HttpClientFactory = handler is null ? new HttpClientFactory() : new HandlerFactory(handler),
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
            throw new CloudAuthorizationException("Google no longer honours the token.", ex);
        }
    }

    // A handler given by a test: Google's clients dispose what they are given, and this is used for several calls.
    private sealed class HandlerFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new SharedHandler(handler);
    }

    private sealed class SharedHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _invoker = new(inner, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _invoker.SendAsync(request, cancellationToken);
    }
}
