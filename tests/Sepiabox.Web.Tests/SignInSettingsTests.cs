using Microsoft.Extensions.Configuration;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

public sealed class SignInSettingsTests
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
    public void Return_urls_stay_on_this_site(string? requested, string expected) =>
        Assert.Equal(expected, AuthSetup.LocalReturnUrl(requested));

    [Fact]
    public void No_configured_client_is_not_an_error()
    {
        var providers = SignInProviders.From(new ConfigurationBuilder().Build());

        Assert.False(providers.Any);
    }

    [Theory]
    [InlineData("Auth:Google:ClientId")]
    [InlineData("Auth:Google:ClientSecret")]
    [InlineData("Auth:Microsoft:ClientId")]
    [InlineData("Auth:Microsoft:ClientSecret")]
    public void A_client_with_only_half_of_its_settings_stops_the_start(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = "x" }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => SignInProviders.From(configuration));
        Assert.Contains(key[..key.LastIndexOf(':')], error.Message, StringComparison.Ordinal);
    }
}
