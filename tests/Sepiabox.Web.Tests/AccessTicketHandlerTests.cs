using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

public sealed class AccessTicketHandlerTests
{
    [Fact]
    public async Task Creates_no_session_and_redirects_to_sem_acesso_when_the_email_is_not_on_the_list()
    {
        var context = TicketFor(TestUsers.Stranger);

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        // Handled means the remote handler stops here: it never signs in, so no cookie is written.
        Assert.True(context.Result?.Handled);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/sem-acesso", context.Response.Headers.Location.ToString());
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task Refuses_a_sign_in_that_carries_no_email()
    {
        var context = TicketFor(email: null);

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal("/sem-acesso", context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("ANA@Example.COM")]
    public async Task Lets_the_sign_in_continue_when_the_email_is_on_the_list(string email)
    {
        var context = TicketFor(email, microsoft: true);

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        Assert.Null(context.Result);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    private static TicketReceivedContext TicketFor(string? email, bool microsoft = false)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .AddSingleton(new AccessList([TestUsers.Allowed]))
                .BuildServiceProvider(),
        };
        Claim[] claims = email is null ? [] : [new Claim(ClaimTypes.Email, email)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "oauth"));

        if (microsoft)
        {
            var scheme = new AuthenticationScheme(MicrosoftAccountDefaults.AuthenticationScheme, null, typeof(MicrosoftAccountHandler));
            return new TicketReceivedContext(http, scheme, new MicrosoftAccountOptions(), new AuthenticationTicket(principal, scheme.Name));
        }

        var google = new AuthenticationScheme(GoogleDefaults.AuthenticationScheme, null, typeof(GoogleHandler));
        return new TicketReceivedContext(http, google, new GoogleOptions(), new AuthenticationTicket(principal, google.Name));
    }
}
