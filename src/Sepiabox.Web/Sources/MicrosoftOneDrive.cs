using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Sepiabox.Catalog;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Sources;

/// <summary>
/// Microsoft's side of <see cref="IOneDriveClient"/> and <see cref="IOneDriveAuthorization"/> (S-039, D-116): the Microsoft identity
/// platform for personal accounts (the <c>consumers</c> tenant) and Microsoft Graph, over plain HTTP, with an OAuth client of its own
/// (<c>OneDrive:ClientId</c>, <c>OneDrive:ClientSecret</c>, from Key Vault), not the sign-in's: Microsoft sign-in is a different
/// feature, and the same secrets would switch it on. Tested against a fake <see cref="HttpMessageHandler"/>. A failure is thrown without
/// the request, a token or an address in it.
/// </summary>
public sealed class MicrosoftOneDrive(OAuthClient? client, HttpMessageHandler? handler = null) : IOneDriveClient, IOneDriveAuthorization
{
    /// <summary>The scopes: the files, a refresh token, and the account's address (<c>User.Read</c>).</summary>
    public const string Scope = "Files.Read offline_access User.Read";

    private const string AuthorizationEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string GraphRoot = "https://graph.microsoft.com/v1.0";
    private const string DownloadUrlProperty = "@microsoft.graph.downloadUrl";

    private readonly HttpClient _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);

    public bool IsConfigured => client is not null;

    public Uri AuthorizationUri(string redirectUri, string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = (client ?? throw new InvalidOperationException("OneDrive is not configured.")).ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = Scope,
            ["prompt"] = "select_account",
            ["state"] = state,
        };
        return new Uri(AuthorizationEndpoint + "?" + string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")));
    }

    public async Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        }, cancellationToken);
        return token.RefreshToken ?? throw new CloudAuthorizationException("Microsoft sent no refresh token.");
    }

    public Task<OneDriveWebToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        TokenAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, cancellationToken);

    public async Task<string> GetAccountEmailAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var json = await GetJsonAsync(accessToken, $"{GraphRoot}/me?$select=mail,userPrincipalName", cancellationToken)
            ?? throw new InvalidOperationException("Microsoft did not say whose OneDrive this is.");
        var address = String(json.RootElement, "mail") ?? String(json.RootElement, "userPrincipalName")
            ?? throw new InvalidOperationException("Microsoft did not say whose OneDrive this is.");
        return address.Trim().ToLowerInvariant();
    }

    public async Task<DriveMediaCount> CountMediaAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        int photos = 0, videos = 0;
        string? next = $"{GraphRoot}/me/drive/root/delta?$select=id,file,deleted&$top=200";
        while (next is not null)
        {
            using var json = await GetJsonAsync(accessToken, next, cancellationToken)
                ?? throw new HttpRequestException("Microsoft Graph did not find the drive.", null, HttpStatusCode.NotFound);
            if (json.RootElement.TryGetProperty("value", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("deleted", out _) || !item.TryGetProperty("file", out var file) || String(file, "mimeType") is not { } mime)
                    {
                        continue;
                    }

                    if (mime.StartsWith("video/", StringComparison.Ordinal))
                    {
                        videos++;
                    }
                    else if (mime.StartsWith("image/", StringComparison.Ordinal))
                    {
                        photos++;
                    }
                }
            }

            next = String(json.RootElement, "@odata.nextLink") is { } link && IsGraph(link) ? link : null;
        }

        return new DriveMediaCount(photos, videos);
    }

    public async Task<DriveFileDownload?> OpenFileAsync(string accessToken, string itemId, CancellationToken cancellationToken = default)
    {
        using var json = await GetJsonAsync(
            accessToken, $"{GraphRoot}/me/drive/items/{Uri.EscapeDataString(itemId)}?$select=name,size,file,deleted,{DownloadUrlProperty}", cancellationToken);
        if (json is null || json.RootElement.TryGetProperty("deleted", out _))
        {
            return null;
        }

        if (String(json.RootElement, DownloadUrlProperty) is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var name = String(json.RootElement, "name") ?? "original";
        var mime = json.RootElement.TryGetProperty("file", out var file) ? String(file, "mimeType") : null;
        long? size = json.RootElement.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : null;
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new HttpRequestException("OneDrive could not be reached to download the file.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            return status == HttpStatusCode.NotFound ? null : throw new HttpRequestException($"OneDrive answered {(int)status} to the download.", null, status);
        }

        // The stream is the response's: it is released with the download.
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new DriveFileDownload(stream, name, mime ?? "application/octet-stream", size, response);
    }

    private async Task<OneDriveWebToken> TokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var oauth = client ?? throw new InvalidOperationException("OneDrive is not configured.");
        form["client_id"] = oauth.ClientId;
        form["client_secret"] = oauth.ClientSecret;
        form["scope"] = Scope;
        using var body = new FormUrlEncodedContent(form);
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(new Uri(TokenEndpoint), body, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new HttpRequestException("Microsoft could not be reached.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ErrorOf(text) == "invalid_grant"
                    ? new CloudAuthorizationException("Microsoft refused the grant.")
                    : new HttpRequestException($"Microsoft answered {(int)response.StatusCode} to the token request.", null, response.StatusCode);
            }

            using var json = JsonDocument.Parse(text);
            var access = String(json.RootElement, "access_token");
            var refresh = String(json.RootElement, "refresh_token");
            return string.IsNullOrEmpty(access)
                ? throw new HttpRequestException("Microsoft sent no access token.")
                : new OneDriveWebToken(access, string.IsNullOrEmpty(refresh) ? null : refresh);
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(string accessToken, string uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new HttpRequestException("Microsoft Graph could not be reached.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new CloudAuthorizationException("Microsoft Graph refused the access token.");
            }

            return response.IsSuccessStatusCode
                ? JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
                : throw new HttpRequestException($"Microsoft Graph answered {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }

    private static bool IsGraph(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "graph.microsoft.com";

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ErrorOf(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return String(json.RootElement, "error");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
