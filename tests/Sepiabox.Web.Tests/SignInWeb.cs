using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The app for the sign-in tests, with an in-memory library: no database, storage or OAuth client is
/// reached. The library holds <see cref="Photo"/>, so a request that got past sign-in would be redirected
/// to storage.
/// </summary>
internal static partial class SignInWeb
{
    public static readonly string Photo = Ids.Of(1);

    /// <summary>Fake clients: the app builds the providers' URLs from them, and no test follows those URLs.</summary>
    public static readonly Dictionary<string, string> BothClients = new()
    {
        ["Auth:Google:ClientId"] = "google-client.apps.example.com",
        ["Auth:Google:ClientSecret"] = "not-a-secret",
        ["Auth:Microsoft:ClientId"] = "00000000-0000-0000-0000-000000000000",
        ["Auth:Microsoft:ClientSecret"] = "not-a-secret",
    };

    public static WebFactory Create(
        string environment = "Development",
        string accessEmails = TestUsers.Allowed,
        Dictionary<string, string>? settings = null,
        DirectoryInfo? keyRing = null)
    {
        var library = new FakeLibrary();
        library.Assets[Photo] = Details.Photo(Photo);
        return new WebFactory(library)
        {
            Environment = environment,
            AccessEmails = accessEmails,
            Settings = settings ?? [],
            KeyRing = keyRing,
        };
    }

    public static Uri Url(string path) => new(path, UriKind.Relative);

    public static SetCookieHeaderValue? SessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? SetCookieHeaderValue.ParseList(cookies.ToList()).FirstOrDefault(cookie => cookie.Name == AuthSetup.CookieName)
            : null;

    /// <summary>Opens <paramref name="page"/> as a browser would and returns the antiforgery token of its form.</summary>
    public static async Task<string> FormTokenAsync(HttpClient http, string page)
    {
        var html = await http.GetStringAsync(Url(page));
        var match = AntiforgeryToken().Match(html);
        Assert.True(match.Success, $"{page} has no antiforgery token.");
        return match.Groups["token"].Value;
    }

    /// <summary>Fills in the e-mail on /entrar and submits it, as a developer would.</summary>
    public static async Task<HttpResponseMessage> DevLoginAsync(HttpClient http, string email, string returnUrl = "/")
    {
        var token = await FormTokenAsync(http, "/entrar");
        return await http.PostAsync(Url("/dev-login"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["email"] = email,
            ["returnUrl"] = returnUrl,
        }));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
