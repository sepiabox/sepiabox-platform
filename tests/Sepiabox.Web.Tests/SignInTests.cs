using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The access list on the real app, with an in-memory library: no database, storage or OAuth client is
/// reached. The library holds a photo, so a request that got past sign-in would be redirected to storage.
/// </summary>
public sealed partial class SignInTests
{
    private static readonly string Photo = Ids.Of(1);

    private static readonly Dictionary<string, string> BothClients = new()
    {
        ["Auth:Google:ClientId"] = "google-client.apps.example.com",
        ["Auth:Google:ClientSecret"] = "not-a-secret",
        ["Auth:Microsoft:ClientId"] = "00000000-0000-0000-0000-000000000000",
        ["Auth:Microsoft:ClientSecret"] = "not-a-secret",
    };

    [Fact]
    public async Task Redirects_an_anonymous_request_for_the_timeline_to_entrar()
    {
        await using var web = Web();
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
        await using var web = Web(accessEmails: "Ana@EXAMPLE.com; BRUNO@example.com");
        using var http = web.CreateSignedInClient(email);

        using var response = await http.GetAsync(Url("/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sair", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("thumb-256")]
    [InlineData("original")]
    public async Task Answers_403_to_media_for_a_signed_in_user_not_on_the_list(string media)
    {
        await using var web = Web();
        using var http = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var response = await http.GetAsync(Url($"/fotos/{Photo}/{media}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("sig=", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answers_403_to_the_api_for_a_signed_in_user_not_on_the_list()
    {
        await using var web = Web();
        using var http = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var response = await http.GetAsync(Url("/api/anything"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Sends_a_signed_in_user_not_on_the_list_from_a_page_to_sem_acesso()
    {
        await using var web = Web();
        using var http = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var response = await http.GetAsync(Url("/"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sem-acesso", response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData("thumb-256")]
    [InlineData("preview-1280")]
    [InlineData("original")]
    public async Task Challenges_an_anonymous_request_for_media_and_never_redirects_it_to_storage(string media)
    {
        await using var web = Web();
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
        await using var web = Web();
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(Url($"/fotos/{Photo}/thumb-256"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("sig=", response.Headers.Location!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answers_401_to_an_anonymous_api_request_instead_of_a_sign_in_page()
    {
        await using var web = Web();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url("/api/anything"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/entrar")]
    [InlineData("/sem-acesso")]
    [InlineData("/alive")]
    [InlineData("/app.css")]
    public async Task Serves_the_sign_in_pages_probes_and_styles_anonymously(string path)
    {
        await using var web = Web();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Does_not_send_an_anonymous_health_probe_to_sign_in()
    {
        await using var web = Web();
        using var http = web.CreateAnonymousClient();

        // The database behind the fake library does not exist, so the check itself may fail; it must still answer.
        using var response = await http.GetAsync(Url("/health"));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable });
    }

    [Fact]
    public async Task Shows_a_button_for_each_configured_provider_on_entrar()
    {
        await using var web = Web(settings: BothClients);
        using var http = web.CreateAnonymousClient();

        var html = await http.GetStringAsync(Url("/entrar?ReturnUrl=%2Ffotos%2Fabc"));

        Assert.Contains("Entrar com Google", html, StringComparison.Ordinal);
        Assert.Contains("Entrar com Microsoft", html, StringComparison.Ordinal);
        Assert.Contains("href=\"entrar/google?returnUrl=%2Ffotos%2Fabc\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dev-login", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("google", "accounts.google.com", "/o/oauth2/v2/auth")]
    [InlineData("microsoft", "login.microsoftonline.com", "/consumers/oauth2/v2.0/authorize")]
    public async Task Sends_a_provider_button_to_that_provider_asking_which_account(string provider, string host, string path)
    {
        await using var web = Web(settings: BothClients);
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url($"/entrar/{provider}?returnUrl=%2Ffotos%2Fabc"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal(host, location.Host);
        Assert.Equal(path, location.AbsolutePath);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("select_account", query["prompt"]);
        Assert.EndsWith($"/signin-{provider}", query["redirect_uri"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("apple")]
    public async Task Answers_404_for_a_provider_that_is_not_configured(string provider)
    {
        await using var web = Web();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url($"/entrar/{provider}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", TestUsers.Allowed)]
    [InlineData("Staging", null)]
    public async Task Answers_404_for_dev_login_outside_development(string environment, string? signedInAs)
    {
        await using var web = Web(environment: environment);
        using var http = signedInAs is null ? web.CreateAnonymousClient() : web.CreateSignedInClient(signedInAs, followRedirects: false);

        using var response = await http.GetAsync(Url($"/dev-login?email={TestUsers.Allowed}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(SetsSessionCookie(response));
    }

    [Fact]
    public async Task Answers_404_for_dev_login_in_development_once_an_oauth_client_is_configured()
    {
        await using var web = Web(settings: BothClients);
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url($"/dev-login?email={TestUsers.Allowed}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signs_in_with_dev_login_in_development_and_signs_out_with_the_button()
    {
        await using var web = Web();
        using var http = web.CreateAnonymousClient();

        using var login = await http.GetAsync(Url("/dev-login?email=ANA%40example.com&returnUrl=%2F%3Fpagina%3D1"));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/?pagina=1", login.Headers.Location!.OriginalString);
        Assert.True(SetsSessionCookie(login));

        using var timeline = await http.GetAsync(Url("/"));
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
        var token = AntiforgeryToken().Match(await timeline.Content.ReadAsStringAsync()).Groups["token"].Value;

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
        await using var web = Web();
        using var http = web.CreateAnonymousClient();
        using var login = await http.GetAsync(Url($"/dev-login?email={TestUsers.Allowed}"));

        using var signOut = await http.PostAsync(Url("/sair"), new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, signOut.StatusCode);
        using var timeline = await http.GetAsync(Url("/"));
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
    }

    [Fact]
    public async Task Sends_dev_login_for_an_email_not_on_the_list_to_sem_acesso_without_a_session()
    {
        await using var web = Web();
        using var http = web.CreateAnonymousClient();

        using var login = await http.GetAsync(Url($"/dev-login?email={TestUsers.Stranger}"));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/sem-acesso", login.Headers.Location!.OriginalString);
        Assert.False(SetsSessionCookie(login));
    }

    [Fact]
    public async Task Lets_no_one_in_when_the_access_list_is_empty()
    {
        await using var web = Web(accessEmails: "");
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(Url($"/fotos/{Photo}/thumb-256"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static WebFactory Web(string environment = "Development", string accessEmails = TestUsers.Allowed, Dictionary<string, string>? settings = null)
    {
        var library = new FakeLibrary();
        library.Assets[Photo] = Details.Photo(Photo);
        return new WebFactory(library) { Environment = environment, AccessEmails = accessEmails, Settings = settings ?? [] };
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static bool SetsSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(cookie => cookie.StartsWith(AuthSetup.CookieName + "=", StringComparison.Ordinal));

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
