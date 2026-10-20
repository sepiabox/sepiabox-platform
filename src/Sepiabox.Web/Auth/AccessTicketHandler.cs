using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;

namespace Sepiabox.Web.Auth;

/// <summary>
/// The events of the Google and Microsoft handlers. When a provider hands back a signed-in user, before
/// the session cookie is written, an e-mail that is not on the access list (or that Google says is not
/// verified) never gets a cookie: the response goes to <c>/sem-acesso</c>. A sign-in that fails at the
/// provider (cancelled, expired, tampered with) goes back to <c>/entrar</c> with a short message.
/// </summary>
public static partial class AccessTicketHandler
{
    /// <summary>
    /// The claim Google's <c>email_verified</c> (user info v3; v2's <c>verified_email</c> is also read) is
    /// mapped to; required to be true for Google (D-048).
    /// </summary>
    public const string EmailVerifiedClaim = "email_verified";

    public const string LoggerCategory = "Sepiabox.Web.Auth.SignIn";

    public static async Task OnTicketReceivedAsync(TicketReceivedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var principal = context.Principal;
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        var verified = IsEmailVerified(context.Scheme.Name, principal);

        if (verified && services.GetRequiredService<AccessList>().Allows(email))
        {
            // Stays signed in across browser restarts until 14 days without a visit (D-048).
            context.Properties!.IsPersistent = true;
            return;
        }

        // The address itself is not logged: it belongs to someone who is not a user (D-048).
        LogRefused(Logger(services), context.Scheme.Name, verified ? "the e-mail is not on the access list" : "the e-mail is not verified");

        // Someone already signed in who tries another account loses the earlier session too.
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.Response.Redirect(AuthSetup.AppPath(context.Request, AuthSetup.NoAccessPath));
        context.HandleResponse();
    }

    /// <summary>
    /// Whether the provider vouches that the address belongs to the person who signed in; a provider this
    /// code does not know never does.
    /// </summary>
    private static bool IsEmailVerified(string scheme, ClaimsPrincipal? principal) => scheme switch
    {
        // A Google account can carry an address its owner never confirmed: only an explicit "true" counts,
        // and a missing claim is a refusal.
        GoogleDefaults.AuthenticationScheme => string.Equals(principal?.FindFirstValue(EmailVerifiedClaim), "true", StringComparison.Ordinal),
        // Microsoft's consumers endpoint only signs in personal accounts, whose address Microsoft itself
        // owns and confirms, and its user info has no such claim.
        MicrosoftAccountDefaults.AuthenticationScheme => true,
        _ => false,
    };

    public static Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        // Only the exception type and the OAuth error code, reduced to its expected characters: the rest
        // of the callback's query string is free text from whoever sent it.
        LogRemoteFailure(Logger(context.HttpContext.RequestServices), context.Scheme.Name,
            context.Failure?.GetType().Name ?? "unknown", ErrorCode(context.Failure));
        context.Response.Redirect(AuthSetup.AppPath(context.Request, AuthSetup.SignInPath + "?" + AuthSetup.SignInErrorQuery + "=1"));
        context.HandleResponse();
        return Task.CompletedTask;
    }

    // The handler's own messages when the callback carries no OAuth error, as fixed categories.
    private static readonly Dictionary<string, string> KnownFailures = new(StringComparer.Ordinal)
    {
        ["Correlation failed."] = "correlation_failed",
        ["The oauth state was missing or invalid."] = "invalid_state",
        ["Access was denied by the resource owner or by the remote server."] = "access_denied",
    };

    internal static string ErrorCode(Exception? failure)
    {
        var code = failure?.Data["error"] as string;
        if (code is { Length: > 0 and <= 64 } && code.All(c => char.IsAsciiLetterLower(c) || c == '_'))
        {
            return code;
        }

        return failure is not null && KnownFailures.TryGetValue(failure.Message, out var known) ? known : "unknown";
    }

    private static ILogger Logger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a {Provider} sign-in: {Reason}.")]
    private static partial void LogRefused(ILogger logger, string provider, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A {Provider} sign-in failed: {ErrorType}, OAuth error {ErrorCode}.")]
    private static partial void LogRemoteFailure(ILogger logger, string provider, string errorType, string errorCode);
}
