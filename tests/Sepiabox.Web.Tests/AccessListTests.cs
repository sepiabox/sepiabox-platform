using Microsoft.Extensions.Configuration;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

public sealed class AccessListTests
{
    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("ANA@EXAMPLE.COM")]
    [InlineData("Ana@Example.com")]
    [InlineData("  ana@example.com ")]
    public void Allows_an_email_on_the_list_when_only_the_casing_or_spaces_differ(string email)
    {
        var access = new AccessList(["Ana@example.COM"]);

        Assert.True(access.Allows(email));
    }

    [Theory]
    [InlineData("bruno@example.com")]
    [InlineData("ana@example.co")]
    [InlineData("ana@example.com.evil.example")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Refuses_an_email_that_is_not_on_the_list(string? email)
    {
        var access = new AccessList(["ana@example.com"]);

        Assert.False(access.Allows(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ; ")]
    public void Refuses_everyone_when_the_list_is_empty_or_missing(string? configured)
    {
        var access = AccessList.From(Configuration(new() { [AccessList.ConfigurationKey] = configured }));

        Assert.Equal(0, access.Count);
        Assert.False(access.Allows("ana@example.com"));
    }

    [Fact]
    public void Reads_one_value_separated_by_commas_semicolons_or_spaces_as_key_vault_holds_it()
    {
        var access = AccessList.From(Configuration(new()
        {
            [AccessList.ConfigurationKey] = "ana@example.com, bruno@example.com;carla@example.com\ndavi@example.com",
        }));

        Assert.Equal(4, access.Count);
        Assert.All(["ana@example.com", "bruno@example.com", "carla@example.com", "davi@example.com"], email => Assert.True(access.Allows(email)));
    }

    [Fact]
    public void Reads_an_array_as_appsettings_or_user_secrets_hold_it()
    {
        var access = AccessList.From(Configuration(new()
        {
            [$"{AccessList.ConfigurationKey}:0"] = "ana@example.com",
            [$"{AccessList.ConfigurationKey}:1"] = "BRUNO@example.com",
        }));

        Assert.Equal(2, access.Count);
        Assert.True(access.Allows("bruno@example.com"));
    }

    [Fact]
    public void Follows_the_configuration_when_it_reloads()
    {
        var configuration = (IConfigurationRoot)Configuration(new() { [AccessList.ConfigurationKey] = "ana@example.com" });
        var access = AccessList.From(configuration);

        configuration[AccessList.ConfigurationKey] = "bruno@example.com";
        configuration.Reload();

        Assert.False(access.Allows("ana@example.com"));
        Assert.True(access.Allows("bruno@example.com"));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
