using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Sepiabox.Web.Auth;

/// <summary>
/// The events of the Google and Microsoft handlers. When a provider hands back a signed-in user, before
/// the session cookie is written, an e-mail that is not on the access list (or that Google says is not
/// verified) never gets a cookie: the response goes to <c>/sem-acesso</c>. A sign-in that fails at the
/// provider (cancelled, expired, tampered with) goes back to <c>/entrar</c> with a short message.
/// </summary>
public static partial class AccessTicketHandler
{
    /// <summary>The claim Google's <c>verified_email</c> is mapped to (D-048).</summary>
    public const string EmailVerifiedClaim = "email_verified";

    public const string LoggerCategory = "Sepiabox.Web.Auth.SignIn";

    public static async Task OnTicketReceivedAsync(TicketReceivedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var principal = context.Principal;
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        var unverified = principal?.FindFirstValue(EmailVerifiedClaim) is { } verified
            && !string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase);

        if (!unverified && services.GetRequiredService<AccessList>().Allows(email))
        {
            // Stays signed in across browser restarts until 14 days without a visit (D-048).
            context.Properties!.IsPersistent = true;
            return;
        }

        // The address itself is not logged: it belongs to someone who is not a user (D-048).
        LogRefused(Logger(services), context.Scheme.Name, unverified ? "the e-mail is not verified" : "the e-mail is not on the access list");

        // Someone already signed in who tries another account loses the earlier session too.
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.Response.Redirect(AuthSetup.AppPath(context.Request, AuthSetup.NoAccessPath));
        context.HandleResponse();
    }

    public static Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        // Provider messages carry error codes, never the person's address.
        LogRemoteFailure(Logger(context.HttpContext.RequestServices), context.Scheme.Name,
            context.Failure?.GetType().Name ?? "unknown", context.Failure?.Message ?? "");
        context.Response.Redirect(AuthSetup.AppPath(context.Request, AuthSetup.SignInPath + "?" + AuthSetup.SignInErrorQuery + "=1"));
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static ILogger Logger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a {Provider} sign-in: {Reason}.")]
    private static partial void LogRefused(ILogger logger, string provider, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A {Provider} sign-in failed: {ErrorType}: {ErrorMessage}")]
    private static partial void LogRemoteFailure(ILogger logger, string provider, string errorType, string errorMessage);
}
