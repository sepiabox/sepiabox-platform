using Microsoft.Extensions.Configuration;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

public sealed class SignInProvidersTests
{
    [Fact]
    public void Treats_no_configured_client_as_no_provider()
    {
        var providers = SignInProviders.From(new ConfigurationBuilder().Build());

        Assert.False(providers.Any);
    }

    [Theory]
    [InlineData("Auth:Google:ClientId")]
    [InlineData("Auth:Google:ClientSecret")]
    [InlineData("Auth:Microsoft:ClientId")]
    [InlineData("Auth:Microsoft:ClientSecret")]
    public void Stops_the_start_when_a_client_has_only_half_of_its_settings(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = "x" }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => SignInProviders.From(configuration));
        Assert.Contains(key[..key.LastIndexOf(':')], error.Message, StringComparison.Ordinal);
    }
}
