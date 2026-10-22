using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Sepiabox.Web.Sources;

/// <summary>
/// The two addresses of connecting a Google Drive (S-037): <c>/sources/google-drive/connect</c> sends the browser to
/// Google, <c>/sources/google-drive/callback</c> is where Google sends it back; the same two for OneDrive (S-039) under
/// <c>/sources/onedrive</c>, each with a state cookie and a state purpose of its own, so a state made for one is no good for the other.
/// Both answer only to a signed-in person on the access list, like everything else.
/// </summary>
public static class SourceEndpoints
{
    public const string ConnectPath = "/sources/google-drive/connect";
    public const string CallbackPath = "/sources/google-drive/callback";
    public const string OneDriveConnectPath = "/sources/onedrive/connect";
    public const string OneDriveCallbackPath = "/sources/onedrive/callback";
    public const string SourcesPath = "/sources";
    public const string NoteParameter = "note";

    /// <summary>The cookie that carries the state's nonce, so a callback is the answer to this browser's own request.</summary>
    public const string StateCookie = "sepiabox-drive-state";

    public const string OneDriveStateCookie = "sepiabox-onedrive-state";

    public const string StatePurpose = "Sepiabox.Web.ConnectedSources.State.v1";

    public const string OneDriveStatePurpose = "Sepiabox.Web.ConnectedSources.OneDriveState.v1";

    /// <summary>How long a state is good for: the time to consent at Google.</summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public static WebApplication MapSourceEndpoints(this WebApplication app)
    {
        app.MapGet(ConnectPath, (HttpContext http, IConnectedSources sources, IDataProtectionProvider protection) =>
            Connect(http, GoogleDriveService, sources.CanConnectGoogleDrive, sources.GoogleDriveAuthorizationUri, protection));
        app.MapGet(CallbackPath, (HttpContext http, IConnectedSources sources, IDataProtectionProvider protection, string? code, string? state, string? error) =>
            CallbackAsync(http, GoogleDriveService, sources.CompleteGoogleDriveConnectionAsync, protection, code, state, error));
        app.MapGet(OneDriveConnectPath, (HttpContext http, IConnectedSources sources, IDataProtectionProvider protection) =>
            Connect(http, OneDriveService, sources.CanConnectOneDrive, sources.OneDriveAuthorizationUri, protection));
        app.MapGet(OneDriveCallbackPath, (HttpContext http, IConnectedSources sources, IDataProtectionProvider protection, string? code, string? state, string? error) =>
            CallbackAsync(http, OneDriveService, sources.CompleteOneDriveConnectionAsync, protection, code, state, error));
        return app;
    }

    /// <summary>The address the service sends the browser back to: the app's own, whatever the host or base path.</summary>
    public static string RedirectUri(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}{CallbackPath}";

    public static string OneDriveRedirectUri(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}{OneDriveCallbackPath}";

    private static readonly Service GoogleDriveService = new(StateCookie, StatePurpose, "/sources/google-drive", "", RedirectUri);

    private static readonly Service OneDriveService = new(OneDriveStateCookie, OneDriveStatePurpose, "/sources/onedrive", "onedrive-", OneDriveRedirectUri);

    // What differs between the services; the notes of OneDrive carry a prefix (the page says which service a note is about).
    private sealed record Service(string Cookie, string Purpose, string CookiePath, string NotePrefix, Func<HttpRequest, string> Redirect);

    private static IResult Connect(
        HttpContext http, Service service, bool configured, Func<string, string, Uri> authorizationUri, IDataProtectionProvider protection)
    {
        if (!configured)
        {
            return Back(http, service.NotePrefix + "not-configured");
        }

        var email = http.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var state = StateProtector(protection, service.Purpose).Protect($"{nonce}|{email.ToLowerInvariant()}", StateLifetime);
        http.Response.Cookies.Append(service.Cookie, nonce, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            // Lax: the cookie must come back with the service's redirect, a top-level GET from another site.
            SameSite = SameSiteMode.Lax,
            MaxAge = StateLifetime,
            Path = http.Request.PathBase + service.CookiePath,
        });
        return Results.Redirect(authorizationUri(service.Redirect(http.Request), state).AbsoluteUri);
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext http,
        Service service,
        Func<string, string, CancellationToken, Task<SourceOutcome>> complete,
        IDataProtectionProvider protection,
        string? code,
        string? state,
        string? error)
    {
        var nonce = http.Request.Cookies[service.Cookie];
        http.Response.Cookies.Delete(service.Cookie, new CookieOptions { Path = http.Request.PathBase + service.CookiePath });
        if (!StateIsOurs(protection, state, nonce, http.User.FindFirstValue(ClaimTypes.Email), service.Purpose))
        {
            return Back(http, "state");
        }

        if (error is not null)
        {
            return Back(http, service.NotePrefix + (error == "access_denied" ? "denied" : "failed"));
        }

        if (string.IsNullOrEmpty(code))
        {
            return Back(http, service.NotePrefix + "failed");
        }

        var outcome = await complete(code, service.Redirect(http.Request), http.RequestAborted);
        return Back(http, service.NotePrefix + (outcome == SourceOutcome.Done ? "connected" : "failed"));
    }

    /// <summary>Whether <paramref name="state"/> is one this app made in the last 10 minutes for this person and this browser.</summary>
    public static bool StateIsOurs(IDataProtectionProvider protection, string? state, string? nonce, string? email, string purpose = StatePurpose)
    {
        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(nonce))
        {
            return false;
        }

        try
        {
            var parts = StateProtector(protection, purpose).Unprotect(state).Split('|', 2);
            return parts is [var stored, var storedEmail]
                && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(stored), System.Text.Encoding.UTF8.GetBytes(nonce))
                && string.Equals(storedEmail, (email ?? string.Empty).ToLowerInvariant(), StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            // Not ours, damaged, or expired (a time-limited protector says so as a CryptographicException).
            return false;
        }
    }

    public static ITimeLimitedDataProtector StateProtector(IDataProtectionProvider protection, string purpose = StatePurpose) =>
        protection.CreateProtector(purpose).ToTimeLimitedDataProtector();

    private static IResult Back(HttpContext http, string note) =>
        Results.Redirect($"{http.Request.PathBase}{SourcesPath}?{NoteParameter}={note}");
}
