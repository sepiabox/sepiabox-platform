using Microsoft.AspNetCore.Http;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

public sealed class ReturnUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/photos/abc?page=2", "/photos/abc?page=2")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("/\t/evil.example/", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("photos", "/")]
    [InlineData("/sign-in", "/")]
    [InlineData("/SIGN-IN/google?returnUrl=%2F", "/")]
    [InlineData("/no-access", "/")]
    [InlineData("/sign-out", "/")]
    [InlineData("/dev-login", "/")]
    [InlineData("/sign-inx", "/sign-inx")]
    [InlineData("/%73ign-in", "/")]
    [InlineData("/no%2Daccess?x=1", "/")]
    // Not escaped, so it could not go into a Location header; the app's own links are always escaped.
    [InlineData("/photos/é", "/")]
    [InlineData("/photos/%C3%A9", "/photos/%C3%A9")]
    public void Keeps_return_urls_on_this_site(string? requested, string expected) =>
        Assert.Equal(expected, AuthSetup.LocalReturnUrl(new DefaultHttpContext().Request, requested));

    [Theory]
    [InlineData("/sign-in?ReturnUrl=%2F", "/sign-in?ReturnUrl=%2F")]
    [InlineData("/no-access", "/no-access")]
    [InlineData("/photos/abc?page=2", "/photos/abc?page=2")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("/photos/é", "/")]
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
        Assert.Equal("/app/photos/abc", AuthSetup.LocalReturnUrl(request, "/app/photos/abc"));
        Assert.Equal("/app/no-access", AuthSetup.AppPath(request, AuthSetup.NoAccessPath));
        Assert.Equal("/app/", AuthSetup.LocalReturnUrl(request, "/app/sign-in"));
        Assert.Equal("/app/", AuthSetup.LocalReturnUrl(request, "/APP/%73ign-in?ReturnUrl=%2F"));
    }
}
