using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Sepiabox.Web.Auth;

/// <summary>
/// Runs when Google or Microsoft hands back a signed-in user, before the session cookie is written. An
/// e-mail that is not on the access list never gets a cookie: the response goes to <c>/sem-acesso</c>.
/// </summary>
public static partial class AccessTicketHandler
{
    public static Task OnTicketReceivedAsync(TicketReceivedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
        if (services.GetRequiredService<AccessList>().Allows(email))
        {
            return Task.CompletedTask;
        }

        // The address itself is not logged: it belongs to someone who is not a user (D-048).
        LogRefused(services.GetRequiredService<ILogger<AccessList>>(), context.Scheme.Name);
        context.Response.Redirect(context.Request.PathBase + AuthSetup.NoAccessPath);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a {Provider} sign-in: the e-mail is not on the access list.")]
    private static partial void LogRefused(ILogger logger, string provider);
}
