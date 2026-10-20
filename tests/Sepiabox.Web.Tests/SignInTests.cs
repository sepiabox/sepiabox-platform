using System.Net;
using System.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Sepiabox.Web.Auth;
using static Sepiabox.Web.Tests.SignInWeb;

namespace Sepiabox.Web.Tests;

/// <summary>Who reaches pages, media and the API, and how the provider buttons leave for Google or Microsoft.</summary>
public sealed class SignInTests
{
    [Fact]
    public async Task Redirects_an_anonymous_request_for_the_timeline_to_entrar()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url("/"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/entrar", response.Headers.Location!.AbsolutePath);
        Assert.Equal("/", HttpUtility.ParseQueryString(response.Headers.Location.Query)["ReturnUrl"]);
    }

    [Theory]
    [InlineData(TestUsers.Allowed)]
    [InlineData("ANA@Example.COM")]
    [InlineData(TestUsers.Other)]
    public async Task Shows_the_timeline_to_a_user_on_the_list_whatever_the_casing(string email)
    {
        // The list itself is written with other casings and separators, as someone would type it into Key Vault.
        await using var web = Create(accessEmails: "Ana@EXAMPLE.com; BRUNO@example.com");
        using var http = web.CreateSignedInClient(email);

        using var response = await http.GetAsync(Url("/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sair", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("thumb-256")]
    [InlineData("poster")]
    [InlineData("proxy-720p")]
    [InlineData("original")]
    public async Task Answers_403_to_media_for_a_signed_in_user_not_on_the_list(string media)
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var response = await http.GetAsync(Url($"/fotos/{Photo}/{media}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("sig=", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answers_403_to_the_api_for_a_signed_in_user_not_on_the_list()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var response = await http.GetAsync(Url("/api/anything"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Sends_a_signed_in_user_not_on_the_list_from_a_page_to_sem_acesso()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var response = await http.GetAsync(Url("/"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sem-acesso", response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData("thumb-256")]
    [InlineData("preview-1280")]
    [InlineData("poster")]
    [InlineData("proxy-720p")]
    [InlineData("original")]
    public async Task Challenges_an_anonymous_request_for_media_and_never_redirects_it_to_storage(string media)
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url($"/fotos/{Photo}/{media}"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("localhost", location.Host);
        Assert.Equal("/entrar", location.AbsolutePath);
        Assert.DoesNotContain("sig=", location.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("devstoreaccount1", location.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redirects_the_same_media_to_storage_once_signed_in()
    {
        // The control for the test above: the setup would leak a storage link if sign-in let it through.
        await using var web = Create();
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(Url($"/fotos/{Photo}/thumb-256"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("sig=", response.Headers.Location!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answers_401_with_a_challenge_header_to_an_anonymous_api_request()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url("/api/anything"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Cookie", challenge.Scheme);
        Assert.Contains("sign-in=\"/entrar\"", challenge.Parameter, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/entrar")]
    [InlineData("/sem-acesso")]
    [InlineData("/alive")]
    [InlineData("/app.css")]
    public async Task Serves_the_sign_in_pages_probes_and_styles_anonymously(string path)
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Answers_an_anonymous_health_probe_instead_of_sending_it_to_sign_in()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url("/health"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sends_a_user_who_already_has_access_from_entrar_to_where_they_were_going()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(Url("/entrar?ReturnUrl=%2Ffotos%2Fabc"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/fotos/abc", response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData("%2Fentrar")]
    [InlineData("%2Fsem-acesso")]
    [InlineData("%2Fsair")]
    public async Task Sends_a_user_who_already_has_access_home_instead_of_back_to_a_sign_in_page(string returnUrl)
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(Url($"/entrar?ReturnUrl={returnUrl}"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Shows_a_button_for_each_configured_provider_on_entrar()
    {
        await using var web = Create(settings: BothClients);
        using var http = web.CreateAnonymousClient();

        var html = await http.GetStringAsync(Url("/entrar?ReturnUrl=%2Ffotos%2Fabc"));

        Assert.Contains("Entrar com Google", html, StringComparison.Ordinal);
        Assert.Contains("Entrar com Microsoft", html, StringComparison.Ordinal);
        Assert.Contains("href=\"entrar/google?returnUrl=%2Ffotos%2Fabc\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dev-login", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Não foi possível entrar", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("google", "accounts.google.com", "/o/oauth2/v2/auth", "/signin-google")]
    [InlineData("Microsoft", "login.microsoftonline.com", "/consumers/oauth2/v2.0/authorize", "/signin-microsoft")]
    public async Task Sends_a_provider_button_to_that_provider_asking_which_account(string provider, string host, string path, string callback)
    {
        await using var web = Create(settings: BothClients);
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url($"/entrar/{provider}?returnUrl=%2Ffotos%2Fabc"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal(host, location.Host);
        Assert.Equal(path, location.AbsolutePath);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("select_account", query["prompt"]);
        Assert.Equal("http://localhost" + callback, query["redirect_uri"]);
    }

    [Fact]
    public async Task Asks_google_to_return_over_https_to_the_browsers_host_whatever_x_forwarded_host_says()
    {
        await using var web = Create(environment: "Production", settings: BothClients);
        using var http = web.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/entrar/google"));
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        // The ingress keeps the browser's Host; a forwarded host from anyone else must not move the callback.
        request.Headers.Add("X-Forwarded-Host", "evil.example");

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("https://localhost/signin-google", query["redirect_uri"]);
    }

    [Theory]
    [InlineData("Production", CookieSecurePolicy.Always)]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    public async Task Keeps_the_session_in_a_secure_persistent_cookie(string environment, CookieSecurePolicy secure)
    {
        await using var web = Create(environment: environment);

        var cookie = web.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal(AuthSetup.CookieName, cookie.Cookie.Name);
        Assert.Equal(secure, cookie.Cookie.SecurePolicy);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, cookie.Cookie.SameSite);
        Assert.Equal(TimeSpan.FromDays(14), cookie.ExpireTimeSpan);
        Assert.True(cookie.SlidingExpiration);
    }

    [Fact]
    public async Task Checks_the_access_list_again_on_interactive_circuits()
    {
        await using var web = Create();
        await using var scope = web.Services.CreateAsyncScope();

        Assert.IsType<AccessListRevalidation>(scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>());
    }

    [Fact]
    public async Task Attaches_the_access_list_and_failure_events_to_both_providers()
    {
        await using var web = Create(settings: BothClients);

        var google = web.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleDefaults.AuthenticationScheme);
        var microsoft = web.Services.GetRequiredService<IOptionsMonitor<MicrosoftAccountOptions>>().Get(MicrosoftAccountDefaults.AuthenticationScheme);

        Func<Microsoft.AspNetCore.Authentication.TicketReceivedContext, Task> ticket = AccessTicketHandler.OnTicketReceivedAsync;
        Func<Microsoft.AspNetCore.Authentication.RemoteFailureContext, Task> failure = AccessTicketHandler.OnRemoteFailureAsync;
        Assert.Equal(ticket, google.Events.OnTicketReceived);
        Assert.Equal(ticket, microsoft.Events.OnTicketReceived);
        Assert.Equal(failure, google.Events.OnRemoteFailure);
        Assert.Equal(failure, microsoft.Events.OnRemoteFailure);
        Assert.Equal($"{AuthSetup.MicrosoftConsumers}/authorize", microsoft.AuthorizationEndpoint);
        Assert.Equal($"{AuthSetup.MicrosoftConsumers}/token", microsoft.TokenEndpoint);
        Assert.Contains(google.ClaimActions, action => action.ClaimType == AccessTicketHandler.EmailVerifiedClaim);
    }

    [Theory]
    [InlineData("""{"sub":"1","email":"Ana@Example.com","email_verified":true}""", "/fotos/abc")]
    [InlineData("""{"id":"1","email":"ana@example.com","verified_email":true}""", "/fotos/abc")]
    [InlineData("""{"sub":"1","email":"ana@example.com"}""", "/sem-acesso")]
    [InlineData("""{"sub":"1","email":"ana@example.com","email_verified":false}""", "/sem-acesso")]
    [InlineData("""{"sub":"1","email":"stranger@example.com","email_verified":true}""", "/sem-acesso")]
    public async Task Signs_in_through_the_real_google_handler_only_with_a_verified_email_on_the_list(string userInfo, string landsOn)
    {
        using var google = new FakeGoogle(userInfo);
        await using var web = Create(settings: BothClients, googleBackchannel: google);
        using var http = web.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        // The button: the handler writes its correlation cookie and sends the browser to Google with a state.
        using var challenge = await http.GetAsync(Url("/entrar/google?returnUrl=%2Ffotos%2Fabc"));
        var state = HttpUtility.ParseQueryString(challenge.Headers.Location!.Query)["state"];
        var correlation = SetCookieHeaderValue.ParseList(challenge.Headers.GetValues("Set-Cookie").ToList())
            .Single(cookie => cookie.Name.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal));

        // Google sends the browser back with a code; the handler redeems it and reads the user info.
        using var back = new HttpRequestMessage(HttpMethod.Get, Url($"/signin-google?code=fake-code&state={Uri.EscapeDataString(state!)}"));
        back.Headers.Add("Cookie", $"{correlation.Name}={correlation.Value}");
        using var callback = await http.SendAsync(back);

        Assert.Equal([GoogleDefaults.TokenEndpoint, GoogleDefaults.UserInformationEndpoint], google.Calls);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(landsOn, callback.Headers.Location!.OriginalString);
        var session = SessionCookie(callback);
        if (landsOn == "/sem-acesso")
        {
            // At most the cookie that deletes an earlier session.
            Assert.True(session is null || session.Value.Length == 0);
        }
        else
        {
            Assert.True(session!.Value.Length > 0);
            Assert.True(session.Expires > DateTimeOffset.UtcNow.AddDays(13));
        }
    }

    [Theory]
    [InlineData("google")]
    [InlineData("microsoft")]
    public async Task Sends_a_cancelled_sign_in_back_to_entrar_with_a_message(string provider)
    {
        await using var web = Create(settings: BothClients);
        using var http = web.CreateAnonymousClient();

        using var callback = await http.GetAsync(Url($"/signin-{provider}?error=access_denied&state=anything"));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/entrar?erro=1", callback.Headers.Location!.OriginalString);
        Assert.Null(SessionCookie(callback));
        var page = await http.GetStringAsync(Url("/entrar?erro=1"));
        Assert.Contains("Não foi possível entrar", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("apple")]
    public async Task Answers_404_for_a_provider_that_is_not_configured(string provider)
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url($"/entrar/{provider}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Lets_no_one_in_when_the_access_list_is_empty()
    {
        await using var web = Create(accessEmails: "");
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(Url($"/fotos/{Photo}/thumb-256"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
