using System.Net;
using System.Web;
using static Sepiabox.Web.Tests.SignInWeb;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The Portuguese routes of S-009 to S-011 were renamed to English without redirects (S-055, D-050): they are
/// unknown addresses now, and the new sign-in routes are the ones a return URL may not loop back to (D-048).
/// </summary>
public sealed class OldRoutesTests
{
    public static TheoryData<string> OldRoutes() =>
    [
        "/entrar",
        "/entrar/google",
        "/sem-acesso",
        "/sair",
        $"/fotos/{Photo}",
        $"/fotos/{Photo}/thumb-256",
        $"/fotos/{Photo}/proxy-720p",
        $"/fotos/{Photo}/poster",
        $"/fotos/{Photo}/original",
    ];

    [Theory]
    [MemberData(nameof(OldRoutes))]
    public async Task Answers_404_to_an_old_route_when_signed_in(string path)
    {
        await using var web = Create(settings: BothClients);
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OldRoutes))]
    public async Task Sends_an_old_route_to_sign_in_like_any_unknown_address_when_signed_out(string path)
    {
        await using var web = Create(settings: BothClients);
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in", response.Headers.Location!.AbsolutePath);
        Assert.Equal(path, HttpUtility.ParseQueryString(response.Headers.Location.Query)["ReturnUrl"]);
    }

    [Fact]
    public async Task Ignores_the_old_error_query_parameter_on_sign_in()
    {
        await using var web = Create(settings: BothClients);
        using var http = web.CreateAnonymousClient();

        var html = await http.GetStringAsync(Url("/sign-in?erro=1"));

        Assert.DoesNotContain("class=\"error\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/sign-in")]
    [InlineData("/sign-in/google?returnUrl=%2F")]
    [InlineData("/no-access")]
    [InlineData("/sign-out")]
    [InlineData("/dev-login")]
    public async Task Sends_a_dev_login_home_instead_of_back_to_a_new_sign_in_route(string returnUrl)
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await DevLoginAsync(http, TestUsers.Allowed, returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }
}
