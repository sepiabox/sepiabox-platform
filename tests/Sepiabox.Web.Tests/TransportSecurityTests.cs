using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sepiabox.Web.Tests;

/// <summary>HSTS and host filtering in Azure (S-035, D-065).</summary>
public sealed class TransportSecurityTests
{
    private const string DefaultHost = "sepiabox-web.example.francecentral.azurecontainerapps.io";
    private const string CustomDomain = "app.sepiabox.com";

    [Theory]
    [InlineData(DefaultHost)]
    [InlineData(CustomDomain)]
    public async Task Production_responses_carry_strict_transport_security_for_a_year_on_this_host_only(string host)
    {
        await using var web = Web("Production", $"{DefaultHost};{CustomDomain}");
        using var client = Client(web, host);

        using var response = await client.GetAsync(new Uri("/sign-in", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task Development_and_local_hosts_get_no_strict_transport_security()
    {
        await using var development = Web("Development", "*");
        await using var production = Web("Production", "*");
        using var local = Client(development, "localhost");
        using var localInProduction = Client(production, "localhost");

        using var developmentResponse = await local.GetAsync(new Uri("/sign-in", UriKind.Relative));
        using var productionResponse = await localInProduction.GetAsync(new Uri("/sign-in", UriKind.Relative));

        // Development keeps /dev-login and plain http working; localhost is never pinned to HTTPS.
        Assert.False(developmentResponse.Headers.Contains("Strict-Transport-Security"));
        Assert.False(productionResponse.Headers.Contains("Strict-Transport-Security"));
    }

    [Theory]
    [InlineData($"{DefaultHost};{CustomDomain}", DefaultHost, HttpStatusCode.OK)]
    [InlineData($"{DefaultHost};{CustomDomain}", CustomDomain, HttpStatusCode.OK)]
    [InlineData($"{DefaultHost};{CustomDomain}", "example.com", HttpStatusCode.BadRequest)]
    // An empty custom domain leaves an empty entry, which host filtering ignores.
    [InlineData($"{DefaultHost};", DefaultHost, HttpStatusCode.OK)]
    [InlineData($"{DefaultHost};", CustomDomain, HttpStatusCode.BadRequest)]
    public async Task Allowed_hosts_are_the_default_host_and_the_custom_domain_as_the_apphost_sets_them(
        string allowedHosts, string host, HttpStatusCode expected)
    {
        await using var web = Web("Production", allowedHosts);
        using var client = Client(web, host);

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }

    private static WebFactory Web(string environment, string allowedHosts) =>
        new(new FakeLibrary()) { Environment = environment, Settings = new() { ["AllowedHosts"] = allowedHosts } };

    private static HttpClient Client(WebFactory web, string host) =>
        web.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{host}"),
            AllowAutoRedirect = false,
        });
}
