using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

public sealed class AccessTicketHandlerTests
{
    [Fact]
    public async Task Creates_no_session_and_redirects_to_no_access_when_the_email_is_not_on_the_list()
    {
        var context = TicketFor(TestUsers.Stranger);

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        // Handled means the remote handler stops here: it never signs in.
        Assert.True(context.Result?.Handled);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/no-access", context.Response.Headers.Location.ToString());
        // The only session cookie written is the one that deletes an earlier session.
        var cookies = SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.OfType<string>().ToList());
        var cookie = Assert.Single(cookies, c => c.Name == AuthSetup.CookieName);
        Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Refuses_a_sign_in_that_carries_no_email()
    {
        var context = TicketFor(email: null);

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal("/no-access", context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Refuses_a_google_email_on_the_list_unless_google_says_it_is_verified(string? emailVerified)
    {
        var context = TicketFor(TestUsers.Allowed, emailVerified);

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal("/no-access", context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("ana@example.com", "true", false)]
    [InlineData("ANA@Example.COM", "true", false)]
    // Microsoft has no such claim; its consumers endpoint is what makes the address trustworthy.
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
    public async Task Sends_a_failed_sign_in_back_to_sign_in_with_a_message()
    {
        var http = Http();
        http.Request.PathBase = "/app";
        var scheme = new AuthenticationScheme(GoogleDefaults.AuthenticationScheme, null, typeof(GoogleHandler));
        var context = new RemoteFailureContext(http, scheme, new GoogleOptions(), new AuthenticationFailureException("Access was denied by the resource owner or by the remote server."));

        await AccessTicketHandler.OnRemoteFailureAsync(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal("/app/sign-in?error=1", http.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("access_denied", "failed", "access_denied")]
    [InlineData("Access denied for ana@example.com", "failed", "unknown")]
    [InlineData("", "failed", "unknown")]
    [InlineData(null, "Correlation failed.", "correlation_failed")]
    [InlineData(null, "The oauth state was missing or invalid.", "invalid_state")]
    [InlineData(null, "Something about ana@example.com", "unknown")]
    public void Logs_only_a_well_formed_oauth_error_code_or_a_known_failure(string? error, string message, string expected)
    {
        var failure = new AuthenticationFailureException(message);
        failure.Data["error"] = error;

        Assert.Equal(expected, AccessTicketHandler.ErrorCode(failure));
    }

    private static DefaultHttpContext Http()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(new AccessList([TestUsers.Allowed]));
        services.AddAuthentication().AddCookie(options => options.Cookie.Name = AuthSetup.CookieName);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    [Fact]
    public async Task Refuses_a_sign_in_from_a_provider_it_does_not_know()
    {
        var context = TicketFor(TestUsers.Allowed, "true", scheme: "Other");

        await AccessTicketHandler.OnTicketReceivedAsync(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal("/no-access", context.Response.Headers.Location.ToString());
    }

    private static TicketReceivedContext TicketFor(string? email, string? emailVerified = null, bool microsoft = false, string? scheme = null)
    {
        var http = Http();
        List<Claim> claims = email is null ? [] : [new Claim(ClaimTypes.Email, email)];
        if (emailVerified is not null)
        {
            claims.Add(new Claim(AccessTicketHandler.EmailVerifiedClaim, emailVerified));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "oauth"));
        var authenticationScheme = microsoft
            ? new AuthenticationScheme(MicrosoftAccountDefaults.AuthenticationScheme, null, typeof(MicrosoftAccountHandler))
            : new AuthenticationScheme(scheme ?? GoogleDefaults.AuthenticationScheme, null, typeof(GoogleHandler));
        RemoteAuthenticationOptions options = microsoft ? new MicrosoftAccountOptions() : new GoogleOptions();
        return new TicketReceivedContext(http, authenticationScheme, options, new AuthenticationTicket(principal, new AuthenticationProperties(), authenticationScheme.Name));
    }
}
