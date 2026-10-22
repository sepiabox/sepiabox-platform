using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// Google's side of <see cref="IDriveSyncClient"/> (S-038): the Drive API with the OAuth client of the sign-in (<c>Auth:Google</c>, read
/// from Key Vault), with the refresh token of an account the web connected. Tested alone against a fake
/// <see cref="HttpMessageHandler"/>; the sync's own tests use a fake of the interface. A failure is thrown without the request in it, so no
/// address, token or file name reaches a log.
/// </summary>
public sealed class GoogleDriveSyncClient(string clientId, string clientSecret, HttpMessageHandler? handler = null) : IDriveSyncClient
{
    private const string UserId = "sepiabox";

    /// <summary>What the worker asks Google for: read-only, the promise to the family (D-024). Never a wider scope.</summary>
    public static IReadOnlyList<string> Scopes { get; } = [DriveService.Scope.DriveReadonly];

    private const string FileFields = "id,name,mimeType,size,modifiedTime,md5Checksum,parents,webViewLink,ownedByMe";

    // The photos and videos the account owns that are not in the trash, wherever they are in the Drive. Files other people shared with the
    // account are not the family's: their photos, faces and embeddings must not enter the library (D-115).
    private const string MediaQuery = "(mimeType contains 'image/' or mimeType contains 'video/') and trashed = false and 'me' in owners";

    public async Task<string> GetStartPageTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var drive = Service(refreshToken);
        var request = drive.Changes.GetStartPageToken();
        var token = await ExecuteAsync(() => request.ExecuteAsync(cancellationToken));
        return token.StartPageTokenValue ?? throw new HttpRequestException("Drive gave no start page token.");
    }

    public async Task<DriveListPage> ListMediaAsync(string refreshToken, string? pageToken, CancellationToken cancellationToken = default)
    {
        using var drive = Service(refreshToken);
        var request = drive.Files.List();
        request.Q = MediaQuery;
        request.Fields = $"nextPageToken, files({FileFields})";
        request.PageSize = 200;
        request.PageToken = pageToken;
        var files = await ExecuteAsync(() => request.ExecuteAsync(cancellationToken));
        return new DriveListPage([.. (files.Files ?? []).Select(Info)], files.NextPageToken);
    }

    public async Task<DriveChangesPage> ListChangesAsync(string refreshToken, string pageToken, CancellationToken cancellationToken = default)
    {
        using var drive = Service(refreshToken);
        var request = drive.Changes.List(pageToken);
        request.Fields = $"nextPageToken, newStartPageToken, changes(fileId, removed, file({FileFields},trashed))";
        request.IncludeRemoved = true;
        request.RestrictToMyDrive = true;
        request.PageSize = 200;
        var page = await ExecuteAsync(() => request.ExecuteAsync(cancellationToken));
        var changes = new List<DriveChange>();
        foreach (var change in page.Changes ?? [])
        {
            if (change.FileId is null)
            {
                continue;
            }

            if (change.Removed == true || change.File is null || change.File.Trashed == true)
            {
                changes.Add(new DriveChange(change.FileId, true, null));
            }
            else if (IsMedia(change.File.MimeType) && change.File.OwnedByMe != false)
            {
                changes.Add(new DriveChange(change.FileId, false, Info(change.File)));
            }
        }

        return new DriveChangesPage(changes, page.NextPageToken, page.NewStartPageToken);
    }

    public async Task<DriveFolder?> GetFolderAsync(string refreshToken, string folderId, CancellationToken cancellationToken = default)
    {
        using var drive = Service(refreshToken);
        var request = drive.Files.Get(folderId);
        request.Fields = "name,parents";
        try
        {
            var folder = await ExecuteAsync(() => request.ExecuteAsync(cancellationToken));
            return new DriveFolder(folder.Name ?? string.Empty, folder.Parents?.FirstOrDefault());
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DownloadAsync(string refreshToken, string fileId, Stream destination, CancellationToken cancellationToken = default)
    {
        using var drive = Service(refreshToken);
        var request = drive.Files.Get(fileId);
        var progress = await ExecuteAsync(() => request.DownloadAsync(destination, cancellationToken));
        if (progress.Status != DownloadStatus.Completed)
        {
            // The exception of the download is not carried: it may name the request.
            throw new IOException("Drive could not deliver the file.");
        }
    }

    private static bool IsMedia(string? mimeType) =>
        mimeType is not null && (mimeType.StartsWith("image/", StringComparison.Ordinal) || mimeType.StartsWith("video/", StringComparison.Ordinal));

    private static DriveFileInfo Info(Google.Apis.Drive.v3.Data.File file) => new(
        file.Id,
        file.Name ?? string.Empty,
        file.MimeType ?? string.Empty,
        file.Size,
        file.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UnixEpoch,
        file.Md5Checksum,
        file.Parents is { } parents ? [.. parents] : [],
        file.WebViewLink);

    private GoogleAuthorizationCodeFlow Flow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
        Scopes = [.. Scopes],
        HttpClientFactory = handler is null ? new HttpClientFactory() : new HandlerFactory(handler),
    });

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
            throw new DriveAuthorizationException("Google no longer honours the token.", ex);
        }
        catch (TokenResponseException ex)
        {
            // Any other refusal of the token request is not the account's: reported without what Google said.
            throw new HttpRequestException($"Google answered {(int?)ex.StatusCode ?? 0} to the token request.", null, ex.StatusCode);
        }
        catch (GoogleApiException ex)
        {
            throw new HttpRequestException($"Drive answered {(int)ex.HttpStatusCode}.", null, ex.HttpStatusCode);
        }
    }

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
