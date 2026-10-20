namespace Sepiabox.Web.Auth;

/// <summary>
/// Which OAuth clients are configured. Real clients are a human prerequisite (docs/operations.md), so the
/// app starts without them; a client with only half of its settings is a mistake and stops the start.
/// </summary>
public sealed record SignInProviders(OAuthClient? Google, OAuthClient? Microsoft)
{
    public const string GoogleRoute = "google";
    public const string MicrosoftRoute = "microsoft";

    public bool Any => Google is not null || Microsoft is not null;

    public static SignInProviders From(IConfiguration configuration) =>
        new(OAuthClient.From(configuration, "Auth:Google"), OAuthClient.From(configuration, "Auth:Microsoft"));
}

public sealed record OAuthClient(string ClientId, string ClientSecret)
{
    public static OAuthClient? From(IConfiguration configuration, string section)
    {
        var clientId = configuration[$"{section}:ClientId"];
        var clientSecret = configuration[$"{section}:ClientSecret"];
        return (string.IsNullOrWhiteSpace(clientId), string.IsNullOrWhiteSpace(clientSecret)) switch
        {
            (true, true) => null,
            (false, false) => new OAuthClient(clientId!, clientSecret!),
            (true, false) => throw new InvalidOperationException($"{section}:ClientSecret is set but {section}:ClientId is missing."),
            (false, true) => throw new InvalidOperationException($"{section}:ClientId is set but {section}:ClientSecret is missing."),
        };
    }
}
