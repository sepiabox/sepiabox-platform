using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Sepiabox.Catalog;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// Microsoft's side of <see cref="IOneDriveSyncClient"/> (S-039): the Microsoft identity platform for personal accounts (the
/// <c>consumers</c> tenant) and Microsoft Graph, over plain HTTP so the tests can put a fake <see cref="HttpMessageHandler"/> in place of
/// Microsoft. A failure is thrown without the request, a token or an address in it, so none reaches a log.
/// </summary>
public sealed class GraphOneDriveSyncClient(string clientId, string clientSecret, HttpMessageHandler? handler = null, TimeSpan? retryDelay = null) : IOneDriveSyncClient
{
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string GraphRoot = "https://graph.microsoft.com/v1.0";
    private const string DeltaFields = "id,name,size,file,folder,deleted,parentReference,lastModifiedDateTime,webUrl,root";
    private const string DownloadUrlProperty = "@microsoft.graph.downloadUrl";
    private const int MaxAttempts = 4;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(2);

    /// <summary>The scopes the connection asks for (S-039): the files, a refresh token, and the account's address.</summary>
    public const string Scope = "Files.Read offline_access User.Read";

    private readonly HttpClient _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);

    public async Task<OneDriveToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope,
        });
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(new Uri(TokenEndpoint), body, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new HttpRequestException("Microsoft could not be reached to refresh the token.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ErrorOf(text) == "invalid_grant"
                    ? new CloudAuthorizationException("Microsoft no longer honours the token.")
                    : new HttpRequestException($"Microsoft answered {(int)response.StatusCode} to the token refresh.", null, response.StatusCode);
            }

            using var json = JsonDocument.Parse(text);
            var access = json.RootElement.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            var refresh = json.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            return string.IsNullOrEmpty(access)
                ? throw new HttpRequestException("Microsoft sent no access token.")
                : new OneDriveToken(access, string.IsNullOrEmpty(refresh) ? null : refresh);
        }
    }

    public async Task<OneDriveDeltaPage> DeltaAsync(string accessToken, string? link, CancellationToken cancellationToken = default)
    {
        var uri = link is null ? $"{GraphRoot}/me/drive/root/delta?$select={DeltaFields}&$top=200" : SafeGraphLink(link);
        using var json = await GetJsonAsync(accessToken, uri, cancellationToken)
            ?? throw new HttpRequestException("Microsoft Graph did not find the drive.", null, HttpStatusCode.NotFound);
        var changes = new List<OneDriveChange>();
        if (json.RootElement.TryGetProperty("value", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (Read(item) is { } change)
                {
                    changes.Add(change);
                }
            }
        }

        return new OneDriveDeltaPage(changes, String(json.RootElement, "@odata.nextLink"), String(json.RootElement, "@odata.deltaLink"));
    }

    public async Task<OneDriveFolder?> GetFolderAsync(string accessToken, string itemId, CancellationToken cancellationToken = default)
    {
        using var json = await GetJsonAsync(accessToken, $"{GraphRoot}/me/drive/items/{Uri.EscapeDataString(itemId)}?$select=id,name,parentReference,root", cancellationToken);
        if (json is null)
        {
            return null;
        }

        var root = json.RootElement;
        var isRoot = root.TryGetProperty("root", out _);
        var parent = root.TryGetProperty("parentReference", out var p) ? String(p, "id") : null;
        return new OneDriveFolder(String(root, "name") ?? string.Empty, isRoot ? null : parent);
    }

    public async Task DownloadAsync(string accessToken, string itemId, Stream destination, CancellationToken cancellationToken = default)
    {
        // The address Graph gives is pre-authorized and short-lived: asked for just before, used without the token.
        using var json = await GetJsonAsync(accessToken, $"{GraphRoot}/me/drive/items/{Uri.EscapeDataString(itemId)}?$select={DownloadUrlProperty}", cancellationToken);
        if (json is null || String(json.RootElement, DownloadUrlProperty) is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new IOException("OneDrive gave no address to download the file from.");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new IOException("OneDrive could not be reached to download the file.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new IOException($"OneDrive answered {(int)response.StatusCode} to the download.");
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            await content.CopyToAsync(destination, cancellationToken);
        }
    }

    // A link comes from Graph and is followed as it is, but only if it is Graph's own: a link to anywhere else would carry the token there.
    private static string SafeGraphLink(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "graph.microsoft.com"
            ? link
            : throw new InvalidOperationException("The link is not a Microsoft Graph address.");

    private async Task<JsonDocument?> GetJsonAsync(string accessToken, string uri, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
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

                if (response.StatusCode == HttpStatusCode.Gone)
                {
                    // A delta link Graph no longer honours (too old, or the drive was reset): the caller starts the listing again.
                    throw new DeltaResyncRequiredException();
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // The access token was refused moments after it was issued: the account is no longer honoured.
                    throw new CloudAuthorizationException("Microsoft Graph refused the access token.");
                }

                // Throttled, or a moment's failure: ask again after what Graph says, a few times.
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout && attempt < MaxAttempts)
                {
                    await Task.Delay(retryDelay ?? RetryAfter(response) ?? TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Microsoft Graph answered {(int)response.StatusCode}.", null, response.StatusCode);
                }

                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            }
        }
    }

    // What Graph asks for in Retry-After (seconds or a date), within a limit, so that a throttled run waits and a wrong header cannot hang it.
    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var wait = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return wait is { } w ? TimeSpan.FromSeconds(Math.Clamp(w.TotalSeconds, 0, MaxRetryAfter.TotalSeconds)) : null;
    }

    private static OneDriveChange? Read(JsonElement item)
    {
        if (String(item, "id") is not { } id)
        {
            return null;
        }

        if (item.TryGetProperty("deleted", out _))
        {
            return new OneDriveChange(id, true, null);
        }

        if (!item.TryGetProperty("file", out var file) || String(file, "mimeType") is not { } mime || !IsMedia(mime))
        {
            // A folder, or a file that is neither a photo nor a video.
            return null;
        }

        var hashes = file.TryGetProperty("hashes", out var h) ? h : default;
        var hash = hashes.ValueKind == JsonValueKind.Object ? String(hashes, "sha1Hash") ?? String(hashes, "quickXorHash") : null;
        var parent = item.TryGetProperty("parentReference", out var p) ? String(p, "id") : null;
        var modified = DateTimeOffset.TryParse(String(item, "lastModifiedDateTime"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var when)
            ? when
            : DateTimeOffset.UnixEpoch;
        return new OneDriveChange(
            id, false,
            new OneDriveItem(id, String(item, "name") ?? string.Empty, mime, item.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : null, modified, hash, parent, String(item, "webUrl")));
    }

    private static bool IsMedia(string mimeType) =>
        mimeType.StartsWith("image/", StringComparison.Ordinal) || mimeType.StartsWith("video/", StringComparison.Ordinal);

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

/// <summary>Graph answered 410 Gone to a delta link: the link cannot be used any more and the whole drive has to be listed again (S-039).</summary>
public sealed class DeltaResyncRequiredException() : Exception("Microsoft Graph asks for the delta to start again.");
