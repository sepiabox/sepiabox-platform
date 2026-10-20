using Microsoft.AspNetCore.Http;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

public sealed class ReturnUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/fotos/abc?pagina=2", "/fotos/abc?pagina=2")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("/\t/evil.example/", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("fotos", "/")]
    [InlineData("/entrar", "/")]
    [InlineData("/ENTRAR/google?returnUrl=%2F", "/")]
    [InlineData("/sem-acesso", "/")]
    [InlineData("/sair", "/")]
    [InlineData("/dev-login", "/")]
    [InlineData("/entrarx", "/entrarx")]
    [InlineData("/%65ntrar", "/")]
    [InlineData("/sem%2Dacesso?x=1", "/")]
    // Not escaped, so it could not go into a Location header; the app's own links are always escaped.
    [InlineData("/fotos/é", "/")]
    [InlineData("/fotos/%C3%A9", "/fotos/%C3%A9")]
    public void Keeps_return_urls_on_this_site(string? requested, string expected) =>
        Assert.Equal(expected, AuthSetup.LocalReturnUrl(new DefaultHttpContext().Request, requested));

    [Theory]
    [InlineData("/entrar?ReturnUrl=%2F", "/entrar?ReturnUrl=%2F")]
    [InlineData("/sem-acesso", "/sem-acesso")]
    [InlineData("/fotos/abc?pagina=2", "/fotos/abc?pagina=2")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("/fotos/é", "/")]
    [InlineData(null, "/")]
    public void Keeps_any_local_url_for_the_language_switch_sign_in_pages_included(string? requested, string expected) =>
        Assert.Equal(expected, AuthSetup.LocalUrl(new DefaultHttpContext().Request, requested));

    [Fact]
    public void Falls_back_to_the_home_of_an_app_under_a_path_base()
    {
        var request = new DefaultHttpContext().Request;
        request.PathBase = "/app";

        Assert.Equal("/app/", AuthSetup.LocalReturnUrl(request, "//evil.example/"));
        // A ReturnUrl from the cookie handler already carries the path base and is kept as it is.
        Assert.Equal("/app/fotos/abc", AuthSetup.LocalReturnUrl(request, "/app/fotos/abc"));
        Assert.Equal("/app/sem-acesso", AuthSetup.AppPath(request, AuthSetup.NoAccessPath));
        Assert.Equal("/app/", AuthSetup.LocalReturnUrl(request, "/app/entrar"));
        Assert.Equal("/app/", AuthSetup.LocalReturnUrl(request, "/APP/%65ntrar?ReturnUrl=%2F"));
    }
}
