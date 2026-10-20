using System.Net;
using Microsoft.AspNetCore.Http;
using static Sepiabox.Web.Tests.SignInWeb;

namespace Sepiabox.Web.Tests;

/// <summary>
/// <c>/dev-login</c> per environment, signing out, and a real session cookie that outlives its e-mail's
/// place on the list.
/// </summary>
public sealed class DevLoginTests
{
    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", TestUsers.Allowed)]
    [InlineData("Staging", null)]
    public async Task Answers_404_for_dev_login_outside_development(string environment, string? signedInAs)
    {
        await using var web = Create(environment: environment);
        using var http = signedInAs is null ? web.CreateAnonymousClient() : web.CreateSignedInClient(signedInAs, followRedirects: false);

        using var get = await http.GetAsync(Url($"/dev-login?email={TestUsers.Allowed}"));
        using var post = await http.PostAsync(Url("/dev-login"), new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = TestUsers.Allowed }));

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Null(SessionCookie(get));
        Assert.Null(SessionCookie(post));
    }

    [Fact]
    public async Task Answers_404_for_dev_login_in_development_once_an_oauth_client_is_configured()
    {
        await using var web = Create(settings: BothClients);
        using var http = web.CreateAnonymousClient();

        using var response = await http.PostAsync(Url("/dev-login"), new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = TestUsers.Allowed }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Answers_404_for_a_dev_login_link_in_development()
    {
        // A GET would let any page sign a visitor in with a link or an <img>.
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url($"/dev-login?email={TestUsers.Allowed}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("192.0.2.10", null, "localhost", false)]
    // Claiming loopback in a header changes nothing: Development never reads forwarded headers...
    [InlineData("192.0.2.10", "127.0.0.1", "localhost", false)]
    // ...and even with ASPNETCORE_FORWARDEDHEADERS_ENABLED, a forwarded request is not a local browser.
    [InlineData("192.0.2.10", "127.0.0.1", "localhost", true)]
    [InlineData("127.0.0.1", null, "app.example.com", false)]
    public async Task Answers_404_for_dev_login_from_another_computer_or_through_a_proxy(string remote, string? forwardedFor, string host, bool forwardedHeadersEnabled)
    {
        await using var web = Create(settings: forwardedHeadersEnabled ? new() { ["FORWARDEDHEADERS_ENABLED"] = "true" } : null);

        var context = await web.Server.SendAsync(http =>
        {
            http.Connection.RemoteIpAddress = IPAddress.Parse(remote);
            http.Request.Method = HttpMethods.Post;
            http.Request.Host = new HostString(host);
            http.Request.Path = "/dev-login";
            http.Request.ContentType = "application/x-www-form-urlencoded";
            if (forwardedFor is not null)
            {
                http.Request.Headers["X-Forwarded-For"] = forwardedFor;
            }

            http.Request.Body = new MemoryStream("email=ana%40example.com"u8.ToArray());
        });

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.DoesNotContain(context.Response.Headers.SetCookie, cookie => cookie!.StartsWith("sepiabox-auth=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_dev_login_without_the_antiforgery_token()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();
        await FormTokenAsync(http, "/entrar");

        using var response = await http.PostAsync(Url("/dev-login"), new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = TestUsers.Allowed }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(SessionCookie(response));
    }

    [Fact]
    public async Task Signs_in_with_dev_login_in_development_and_signs_out_with_the_button()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var login = await DevLoginAsync(http, "ANA@example.com", "/?pagina=1");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/?pagina=1", login.Headers.Location!.OriginalString);
        // A persistent cookie (with an expiry) that survives closing the browser.
        Assert.True(SessionCookie(login)!.Expires > DateTimeOffset.UtcNow.AddDays(13));

        var token = await FormTokenAsync(http, "/");
        using var signOut = await http.PostAsync(Url("/sair"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, signOut.StatusCode);
        Assert.Equal("/entrar", signOut.Headers.Location!.OriginalString);

        using var after = await http.GetAsync(Url("/"));
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Equal("/entrar", after.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Refuses_to_sign_out_without_the_antiforgery_token()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();
        using var login = await DevLoginAsync(http, TestUsers.Allowed);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        // The page holds the form, so the browser already has the antiforgery cookie; only the field is missing.
        await FormTokenAsync(http, "/");

        using var signOut = await http.PostAsync(Url("/sair"), new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, signOut.StatusCode);
        using var timeline = await http.GetAsync(Url("/"));
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
    }

    [Fact]
    public async Task Sends_dev_login_for_an_email_not_on_the_list_to_sem_acesso_without_a_session()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var login = await DevLoginAsync(http, TestUsers.Stranger);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/sem-acesso", login.Headers.Location!.OriginalString);
        Assert.Null(SessionCookie(login));
    }

    [Fact]
    public async Task Answers_403_and_sem_acesso_to_a_real_session_cookie_whose_email_left_the_list()
    {
        var keyRing = Directory.CreateTempSubdirectory("sepiabox-keys-");
        try
        {
            string cookie;
            await using (var before = Create(keyRing: keyRing))
            {
                using var http = before.CreateAnonymousClient();
                using var login = await DevLoginAsync(http, TestUsers.Allowed);
                var session = SessionCookie(login)!;
                cookie = $"{session.Name}={session.Value}";
            }

            // The next revision, with the same keys, no longer lists that e-mail.
            await using var after = Create(accessEmails: TestUsers.Other, keyRing: keyRing);
            using var browser = after.CreateClientWithCookie(cookie);

            using var thumbnail = await browser.GetAsync(Url($"/fotos/{Photo}/thumb-256"));
            using var original = await browser.GetAsync(Url($"/fotos/{Photo}/original"));
            using var api = await browser.GetAsync(Url("/api/anything"));
            using var page = await browser.GetAsync(Url("/"));

            Assert.Equal(HttpStatusCode.Forbidden, thumbnail.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, original.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
            Assert.Equal("/sem-acesso", page.Headers.Location!.AbsolutePath);
        }
        finally
        {
            keyRing.Delete(recursive: true);
        }
    }
}
