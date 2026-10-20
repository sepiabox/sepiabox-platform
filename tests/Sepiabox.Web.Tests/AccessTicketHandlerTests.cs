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

        // Handled means the remote handler stops here: it never signs in.
        Assert.True(context.Result?.Handled);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/sem-acesso", context.Response.Headers.Location.ToString());
        // The only session cookie written is the one that deletes an earlier session.
        var cookie = Assert.Single(context.Response.Headers.SetCookie, c => c!.StartsWith(AuthSetup.CookieName + "=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_a_sign_in_that_carries_no_email()
    {
        var context = TicketFor(email: null);

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal("/sem-acesso", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Refuses_a_google_email_on_the_list_that_google_says_is_not_verified()
    {
        var context = TicketFor(TestUsers.Allowed, emailVerified: "false");

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal("/sem-acesso", context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("ana@example.com", null, false)]
    [InlineData("ANA@Example.COM", "true", false)]
    [InlineData("ana@example.com", null, true)]
    public async Task Lets_a_sign_in_on_the_list_continue_and_keeps_it_across_browser_restarts(string email, string? emailVerified, bool microsoft)
    {
        var context = TicketFor(email, emailVerified, microsoft);

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        Assert.Null(context.Result);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        Assert.True(context.Properties!.IsPersistent);
    }

    [Fact]
    public async Task Sends_a_failed_sign_in_back_to_entrar_with_a_message()
    {
        var http = Http();
        http.Request.PathBase = "/app";
        var scheme = new AuthenticationScheme(GoogleDefaults.AuthenticationScheme, null, typeof(GoogleHandler));
        var context = new RemoteFailureContext(http, scheme, new GoogleOptions(), new AuthenticationFailureException("Access was denied by the resource owner or by the remote server."));

        await AccessTicketHandler.OnRemoteFailureAsync(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal("/app/entrar?erro=1", http.Response.Headers.Location.ToString());
    }

    private static DefaultHttpContext Http()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(new AccessList([TestUsers.Allowed]));
        services.AddAuthentication().AddCookie(options => options.Cookie.Name = AuthSetup.CookieName);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static TicketReceivedContext TicketFor(string? email, string? emailVerified = null, bool microsoft = false)
    {
        var http = Http();
        List<Claim> claims = email is null ? [] : [new Claim(ClaimTypes.Email, email)];
        if (emailVerified is not null)
        {
            claims.Add(new Claim(AccessTicketHandler.EmailVerifiedClaim, emailVerified));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "oauth"));
        var scheme = microsoft
            ? new AuthenticationScheme(MicrosoftAccountDefaults.AuthenticationScheme, null, typeof(MicrosoftAccountHandler))
            : new AuthenticationScheme(GoogleDefaults.AuthenticationScheme, null, typeof(GoogleHandler));
        RemoteAuthenticationOptions options = microsoft ? new MicrosoftAccountOptions() : new GoogleOptions();
        return new TicketReceivedContext(http, scheme, options, new AuthenticationTicket(principal, new AuthenticationProperties(), scheme.Name));
    }
}
