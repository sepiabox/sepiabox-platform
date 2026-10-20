using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The real web app, on a test catalog database or on an in-memory <see cref="FakeLibrary"/> (no Docker).
/// Blob links are signed offline with the emulator's well-known key, so nothing is fetched from storage.
/// Requests are signed in through <see cref="TestSignIn"/>: a client made for an e-mail sends it in a
/// header, while anything without the header goes through the app's real cookie.
/// </summary>
internal sealed class WebFactory : WebApplicationFactory<Program>
{
    private readonly string _catalogConnectionString;
    private readonly FakeLibrary? _library;

    public WebFactory(string catalogConnectionString) => _catalogConnectionString = catalogConnectionString;

    /// <summary>The app with an in-memory library instead of PostgreSQL.</summary>
    public WebFactory(FakeLibrary library)
    {
        // Never opened: every query goes to the fake.
        _catalogConnectionString = "Host=localhost;Database=unused";
        _library = library;
    }

    public string Environment { get; init; } = "Development";

    /// <summary>The value of <c>Access:Emails</c>, as Key Vault would hold it.</summary>
    public string AccessEmails { get; init; } = TestUsers.Allowed;

    /// <summary>Extra configuration, such as OAuth clients (fake ones: no test ever reaches Google or Microsoft).</summary>
    public Dictionary<string, string> Settings { get; init; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment(Environment)
            .UseSetting("ConnectionStrings:catalog", _catalogConnectionString)
            .UseSetting("ConnectionStrings:blobs", "UseDevelopmentStorage=true")
            .UseSetting("Access:Emails", AccessEmails);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestSignIn>(TestSignIn.SchemeName, null);
            // Only authentication changes: challenges and 403s still go through the app's cookie handler.
            services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = TestSignIn.SchemeName);
            if (_library is not null)
            {
                services.AddSingleton<ILibraryQueries>(_library);
            }
        });
    }

    /// <summary>A client signed in as <paramref name="email"/>, whether or not it is on the list.</summary>
    public HttpClient CreateSignedInClient(string email = TestUsers.Allowed, bool followRedirects = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects });
        client.DefaultRequestHeaders.Add(TestSignIn.Header, email);
        return client;
    }

    /// <summary>A client with no sign-in that reports redirects instead of following them.</summary>
    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

internal static class TestUsers
{
    public const string Allowed = "ana@example.com";
    public const string Other = "bruno@example.com";
    public const string Stranger = "stranger@example.com";
}

/// <summary>
/// Signs a request in as the e-mail in <see cref="Header"/>, as if a provider had vouched for it; without
/// the header it reads the app's cookie, so <c>/dev-login</c> can be tested end to end.
/// </summary>
internal sealed class TestSignIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var email))
        {
            return Context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, email.ToString())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
