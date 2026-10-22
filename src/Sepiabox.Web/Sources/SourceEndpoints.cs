using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Sepiabox.Web.Sources;

/// <summary>
/// The two addresses of connecting a Google Drive (S-037): <c>/sources/google-drive/connect</c> sends the browser to
/// Google, <c>/sources/google-drive/callback</c> is where Google sends it back. Both answer only to a signed-in person
/// on the access list, like everything else.
/// </summary>
public static class SourceEndpoints
{
    public const string ConnectPath = "/sources/google-drive/connect";
    public const string CallbackPath = "/sources/google-drive/callback";
    public const string SourcesPath = "/sources";
    public const string NoteParameter = "note";

    /// <summary>The cookie that carries the state's nonce, so a callback is the answer to this browser's own request.</summary>
    public const string StateCookie = "sepiabox-drive-state";

    private const string StatePurpose = "Sepiabox.Web.ConnectedSources.State.v1";
    private static readonly TimeSpan s_stateLifetime = TimeSpan.FromMinutes(10);

    public static WebApplication MapSourceEndpoints(this WebApplication app)
    {
        app.MapGet(ConnectPath, Connect);
        app.MapGet(CallbackPath, CallbackAsync);
        return app;
    }

    /// <summary>The address Google sends the browser back to: the app's own, whatever the host or base path.</summary>
    public static string RedirectUri(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}{CallbackPath}";

    private static IResult Connect(HttpContext http, IConnectedSources sources, IDataProtectionProvider protection)
    {
        if (!sources.CanConnectGoogleDrive)
        {
            return Back(http, "not-configured");
        }

        var email = http.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var state = StateProtector(protection).Protect($"{nonce}|{email.ToLowerInvariant()}", s_stateLifetime);
        http.Response.Cookies.Append(StateCookie, nonce, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            // Lax: the cookie must come back with Google's redirect, a top-level GET from another site.
            SameSite = SameSiteMode.Lax,
            MaxAge = s_stateLifetime,
            Path = http.Request.PathBase + "/sources/google-drive",
        });
        return Results.Redirect(sources.GoogleDriveAuthorizationUri(RedirectUri(http.Request), state).AbsoluteUri);
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext http, IConnectedSources sources, IDataProtectionProvider protection, string? code, string? state, string? error)
    {
        var nonce = http.Request.Cookies[StateCookie];
        http.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = http.Request.PathBase + "/sources/google-drive" });
        if (!StateIsOurs(protection, state, nonce, http.User.FindFirstValue(ClaimTypes.Email)))
        {
            return Back(http, "state");
        }

        if (error is not null)
        {
            return Back(http, error == "access_denied" ? "denied" : "failed");
        }

        if (string.IsNullOrEmpty(code))
        {
            return Back(http, "failed");
        }

        var outcome = await sources.CompleteGoogleDriveConnectionAsync(code, RedirectUri(http.Request), http.RequestAborted);
        return Back(http, outcome == SourceOutcome.Done ? "connected" : "failed");
    }

    /// <summary>Whether <paramref name="state"/> is one this app made in the last 10 minutes for this person and this browser.</summary>
    public static bool StateIsOurs(IDataProtectionProvider protection, string? state, string? nonce, string? email)
    {
        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(nonce))
        {
            return false;
        }

        try
        {
            var parts = StateProtector(protection).Unprotect(state).Split('|', 2);
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

    internal static ITimeLimitedDataProtector StateProtector(IDataProtectionProvider protection) =>
        protection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector();

    private static IResult Back(HttpContext http, string note) =>
        Results.Redirect($"{http.Request.PathBase}{SourcesPath}?{NoteParameter}={note}");
}
