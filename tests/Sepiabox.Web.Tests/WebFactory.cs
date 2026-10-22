using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sepiabox.Web.Services;
using Sepiabox.Web.Sources;

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
        // Never opened: every query goes to the fake, and /health uses a fake check.
        _catalogConnectionString = "Host=127.0.0.1;Port=1;Database=unused;Timeout=2";
        _library = library;
    }

    public string Environment { get; init; } = "Development";

    /// <summary>The value of <c>Access:Emails</c>, as Key Vault would hold it.</summary>
    public string AccessEmails { get; init; } = TestUsers.Allowed;

    /// <summary>Extra configuration, such as OAuth clients (fake ones: no test ever reaches Google or Microsoft).</summary>
    public Dictionary<string, string> Settings { get; init; } = [];

    /// <summary>False for the test of the setting's own default.</summary>
    public bool WarmUpOffByDefault { get; init; } = true;

    /// <summary>
    /// A folder for the data protection keys, so two factories can read each other's cookies like two
    /// revisions of the app sharing a key ring.
    /// </summary>
    public DirectoryInfo? KeyRing { get; init; }

    /// <summary>The app's clock, so a test can assert link expiry times exactly.</summary>
    public TimeProvider? Time { get; init; }

    /// <summary>Receives the app's logs as well, so a test can check what it reported.</summary>
    public ILoggerProvider? Logs { get; init; }

    /// <summary>The CLIP text encoder; unset, the real one, pointed at a folder with no model in it.</summary>
    public Sepiabox.Catalog.Search.IQueryEmbedder? QueryEmbedder { get; init; }

    /// <summary>The question translator; unset, the real one, without a key (questions off): no test reaches Anthropic.</summary>
    public Sepiabox.Search.Questions.IQuestionTranslator? QuestionTranslator { get; init; }

    /// <summary>The daily question counter; unset, the catalog's (or an in-memory one with a fake library).</summary>
    public IQuestionQuota? QuestionQuota { get; init; }

    /// <summary>The people editor; unset, the catalog's.</summary>
    public IPeopleEditor? PeopleEditor { get; init; }

    /// <summary>The face recognition switch; unset, the catalog's (or an in-memory one with a fake library).</summary>
    public IFaceSettings? FaceSettings { get; init; }

    /// <summary>The family's places; unset, the catalog's (or an in-memory one with a fake library).</summary>
    public ISavedPlaces? SavedPlaces { get; init; }

    /// <summary>The connected cloud accounts (S-037); unset, the catalog's, with a Drive that is never reached.</summary>
    public IConnectedSources? ConnectedSources { get; init; }

    /// <summary>Answers the Google handler's server-to-server calls (token and user info) instead of Google.</summary>
    public HttpMessageHandler? GoogleBackchannel { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment(Environment)
            .UseSetting("ConnectionStrings:catalog", _catalogConnectionString)
            .UseSetting("ConnectionStrings:blobs", "UseDevelopmentStorage=true")
            .UseSetting("Access:Emails", AccessEmails)
            // Never the developer's or CI's downloaded models: a test chooses with QueryEmbedder.
            .UseSetting("Search:ModelFolder", Path.Combine(Path.GetTempPath(), "sepiabox-tests-no-models"))
            // Never the developer's key from user secrets: a test that needs questions gives a fake translator.
            .UseSetting("Anthropic:ApiKey", string.Empty)

            // Nor the developer's Key Vault: Development loads user secrets, which these settings override (a
            // test that needs a key ring URI sets it in Settings).
            .UseSetting($"ConnectionStrings:{AzureSetup.VaultConnectionName}", string.Empty)
            .UseSetting(AzureSetup.KeyUriKey, string.Empty);
        if (WarmUpOffByDefault)
        {
            // No background question when a page is shown, which would show up in a fake translator's calls (D-101).
            builder.UseSetting("Questions:WarmUp", "false");
        }

        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            // First, so it runs before the host's own filters (such as ASPNETCORE_FORWARDEDHEADERS_ENABLED's),
            // as a real connection's address would.
            services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter, LocalBrowser>());
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestSignIn>(TestSignIn.SchemeName, null);
            // Only authentication changes: challenges and 403s still go through the app's cookie handler.
            services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = TestSignIn.SchemeName);
            if (_library is not null)
            {
                services.AddSingleton<ILibraryQueries>(_library);
                // No database or storage behind the fake: their checks become one that always passes.
                services.PostConfigure<HealthCheckServiceOptions>(options =>
                {
                    foreach (var check in options.Registrations.Where(r => !r.Tags.Contains("live")).ToList())
                    {
                        options.Registrations.Remove(check);
                    }

                    options.Registrations.Add(new HealthCheckRegistration("fake", new AlwaysHealthy(), null, null));
                });
            }

            if (Logs is not null)
            {
                services.AddSingleton(Logs);
            }

            if (QueryEmbedder is not null)
            {
                services.AddSingleton(QueryEmbedder);
            }

            if (QuestionTranslator is not null)
            {
                services.AddSingleton(QuestionTranslator);
            }

            if (PeopleEditor is not null)
            {
                services.AddSingleton(PeopleEditor);
            }

            if (FaceSettings is not null)
            {
                services.AddSingleton(FaceSettings);
            }
            else if (_library is not null)
            {
                services.AddSingleton<IFaceSettings>(new FakeFaceSettings());
            }

            if (SavedPlaces is not null)
            {
                services.AddSingleton(SavedPlaces);
            }
            else if (_library is not null)
            {
                services.AddSingleton<ISavedPlaces>(new FakeSavedPlaces());
            }

            if (ConnectedSources is not null)
            {
                services.AddSingleton(ConnectedSources);
            }
            else if (_library is not null)
            {
                services.AddSingleton<IConnectedSources>(new FakeConnectedSources());
            }

            if (QuestionQuota is not null)
            {
                services.AddSingleton(QuestionQuota);
            }
            else if (_library is not null)
            {
                services.AddSingleton<IQuestionQuota>(new FakeQuestionQuota());
            }

            if (Time is not null)
            {
                services.AddSingleton(Time);
            }

            if (GoogleBackchannel is not null)
            {
                // Backchannel, not BackchannelHttpHandler: by post-configure time the handler's own client exists.
                services.PostConfigure<GoogleOptions>(GoogleDefaults.AuthenticationScheme,
                    options => options.Backchannel = new HttpClient(GoogleBackchannel, disposeHandler: false));
            }

            if (KeyRing is not null)
            {
                services.AddDataProtection().PersistKeysToFileSystem(KeyRing).SetApplicationName("sepiabox-web");
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

    /// <summary>A client with no sign-in that keeps cookies and reports redirects instead of following them.</summary>
    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A client that sends exactly this cookie, as a browser holding an old one would.</summary>
    public HttpClient CreateClientWithCookie(string cookie)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}

/// <summary>
/// TestServer requests have no connection, so they get the address a browser on this computer would have;
/// a test that sets another address keeps it.
/// </summary>
internal sealed class LocalBrowser : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((http, nextMiddleware) =>
        {
            http.Connection.RemoteIpAddress ??= IPAddress.Loopback;
            return nextMiddleware(http);
        });
        next(app);
    };
}

internal sealed class AlwaysHealthy : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy());
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
