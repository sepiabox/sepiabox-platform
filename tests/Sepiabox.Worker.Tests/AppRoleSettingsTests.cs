using Microsoft.Extensions.Configuration;

namespace Sepiabox.Worker.Tests;

public sealed class AppRoleSettingsTests
{
    [Fact]
    public void Settings_are_absent_when_neither_key_is_set()
    {
        Assert.Null(AppRoleSettings.FromConfiguration(Configuration(null, null)));
    }

    [Fact]
    public void Settings_are_read_when_both_keys_are_set()
    {
        var settings = AppRoleSettings.FromConfiguration(Configuration("sepiabox_web", "Abcdefghijklmnop0123456789"));

        Assert.Equal(new AppRoleSettings("sepiabox_web", "Abcdefghijklmnop0123456789"), settings);
    }

    [Theory]
    [InlineData("sepiabox_web", null)]
    [InlineData(null, "Abcdefghijklmnop0123456789")]
    public void Settings_fail_when_only_one_key_is_set(string? name, string? password)
    {
        Assert.Throws<InvalidOperationException>(() => AppRoleSettings.FromConfiguration(Configuration(name, password)));
    }

    [Theory]
    [InlineData("Sepiabox_web", "Abcdefghijklmnop0123456789")]
    [InlineData("web\"; DROP ROLE x; --", "Abcdefghijklmnop0123456789")]
    [InlineData("sepiabox_web\n", "Abcdefghijklmnop0123456789")]
    [InlineData("1web", "Abcdefghijklmnop0123456789")]
    [InlineData("sepiabox_web", "short1A")]
    [InlineData("sepiabox_web", "Abcdefghijklmnop0123456789'")]
    [InlineData("sepiabox_web", "Abcdefghijklmnop0123456789\n")]
    public void Settings_refuse_a_name_or_password_that_would_need_quoting(string name, string password)
    {
        Assert.Throws<ArgumentException>(() => AppRoleSettings.FromConfiguration(Configuration(name, password)));
    }

    private static IConfiguration Configuration(string? name, string? password) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AppRoleSettings.NameKey] = name,
                [AppRoleSettings.PasswordKey] = password,
            })
            .Build();
}
