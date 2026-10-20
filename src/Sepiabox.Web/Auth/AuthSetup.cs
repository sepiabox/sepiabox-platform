using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;

namespace Sepiabox.Web.Auth;

/// <summary>
/// Sign-in with Google or Microsoft, and the access list on every page and endpoint (D-006, D-048).
/// </summary>
/// <remarks>
/// Paths: constants such as <see cref="SignInPath"/> are relative to the app (<c>PathBase</c>) and go through
/// <see cref="AppPath"/>; a return URL is already a full local path, as the cookie handler writes
/// <c>ReturnUrl</c> (with <c>PathBase</c>), and is used as it is once <see cref="LocalReturnUrl"/> accepts it.
/// </remarks>
public static partial class AuthSetup
{
    public const string SignInPath = "/entrar";
    public const string NoAccessPath = "/sem-acesso";
    public const string SignOutPath = "/sair";
    public const string DevLoginPath = "/dev-login";
    public const string CookieName = "sepiabox-auth";
    public const string SignInErrorQuery = "erro";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(14);

    // Personal accounts only: in a work or school tenant, the tenant's administrator chooses the e-mail the
    // app receives. On the consumers endpoint Microsoft itself owns the account, which is what makes the
    // e-mail trustworthy (Google's is checked with email_verified instead).
    internal const string MicrosoftConsumers = "https://login.microsoftonline.com/consumers/oauth2/v2.0";

    public static WebApplicationBuilder AddSepiaboxAuth(this WebApplicationBuilder builder)
    {
        var providers = SignInProviders.From(builder.Configuration);
        var development = builder.Environment.IsDevelopment();
        builder.Services.AddSingleton(providers);
        builder.Services.AddSingleton(services => AccessList.From(
            services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger(AccessTicketHandler.LoggerCategory)));
        builder.Services.AddSingleton<IAuthorizationHandler, AccessListHandler>();

        // In Azure the Container Apps ingress ends TLS and is the only way in, so the forwarded scheme and
        // client address are trusted from any address; without them the OAuth redirect URI would be http.
        // The ingress keeps the browser's Host, so X-Forwarded-Host is never read (D-048). Nothing of this
        // applies in Development, where nothing sits in front.
        if (!development)
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        var authentication = builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                // Local runs may use plain http; everywhere else the cookie never travels without TLS.
                options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = SessionLifetime;
                options.SlidingExpiration = true;
                options.LoginPath = SignInPath;
                options.AccessDeniedPath = NoAccessPath;
                options.Events.OnRedirectToLogin = RedirectToSignInAsync;
                options.Events.OnRedirectToAccessDenied = RedirectToNoAccessAsync;
            });

        if (providers.Google is { } google)
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = google.ClientId;
                options.ClientSecret = google.ClientSecret;
                // Google's user info calls it verified_email (v2) or email_verified (v3).
                options.ClaimActions.MapCustomJson(AccessTicketHandler.EmailVerifiedClaim, EmailVerified);
                options.Events.OnTicketReceived = AccessTicketHandler.OnTicketReceivedAsync;
                options.Events.OnRemoteFailure = AccessTicketHandler.OnRemoteFailureAsync;
            });
        }

        if (providers.Microsoft is { } microsoft)
        {
            authentication.AddMicrosoftAccount(options =>
            {
                options.ClientId = microsoft.ClientId;
                options.ClientSecret = microsoft.ClientSecret;
                options.AuthorizationEndpoint = $"{MicrosoftConsumers}/authorize";
                options.TokenEndpoint = $"{MicrosoftConsumers}/token";
                options.Events.OnTicketReceived = AccessTicketHandler.OnTicketReceivedAsync;
                options.Events.OnRemoteFailure = AccessTicketHandler.OnRemoteFailureAsync;
            });
        }

        // The same policy for [Authorize] (every page, and AuthorizeRouteView inside a circuit) and for
        // everything that says nothing: media, the Blazor hub, unknown addresses.
        var onTheList = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new AccessListRequirement())
            .Build();
        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(onTheList)
            .SetFallbackPolicy(onTheList);
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, AccessListRevalidation>();

        return builder;
    }

    /// <summary>Forwarded headers come first, so everything after sees the browser's scheme and host.</summary>
    public static WebApplication UseSepiaboxForwardedHeaders(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    public static WebApplication MapSepiaboxAuth(this WebApplication app)
    {
        var providers = app.Services.GetRequiredService<SignInProviders>();
        var devLogin = app.Environment.IsDevelopment() && !providers.Any;

        app.MapGet(SignInPath + "/{provider}", Challenge).AllowAnonymous();
        app.MapPost(SignOutPath, SignOutAsync).AllowAnonymous();

        // Mapped everywhere so that it answers 404 to everyone outside Development (left unmapped, an anonymous
        // request would be sent to /entrar like any unknown address); it only signs in with a POST from
        // the /entrar form, in Development, without OAuth clients, from this computer.
        app.MapMethods(DevLoginPath, [HttpMethods.Get, HttpMethods.Post],
            (HttpContext http, IAntiforgery antiforgery, AccessList access) => devLogin
                ? DevLoginAsync(http, antiforgery, access)
                : Task.FromResult(BareNotFound(http)))
            .AllowAnonymous()
            .DisableAntiforgery();

        if (app.Services.GetRequiredService<AccessList>().Count == 0)
        {
            LogEmptyAccessList(app.Logger, AccessList.ConfigurationKey);
        }

        if (!providers.Any && !devLogin)
        {
            LogNoProviders(app.Logger);
        }

        return app;
    }

    /// <summary>A path inside the app, with the app's <c>PathBase</c>.</summary>
    public static string AppPath(HttpRequest request, string path) => request.PathBase + path;

    /// <summary>
    /// A full local path to go back to after sign-in, or the app's home when it is missing, could lead to
    /// another site, or is one of the sign-in pages themselves (which would loop).
    /// </summary>
    public static string LocalReturnUrl(HttpRequest request, string? url) =>
        url is not null
        && url.StartsWith('/')
        && (url.Length == 1 || url[1] is not ('/' or '\\'))
        // Browsers drop tabs and line breaks, which would turn "/\t/host" into "//host".
        && !url.Any(char.IsControl)
        && !IsSignInPage(request, url)
            ? url
            : AppPath(request, "/");

    private static bool IsSignInPage(HttpRequest request, string url)
    {
        var path = new PathString(url.Split('?', '#')[0]);
        return path.StartsWithSegments(request.PathBase, StringComparison.OrdinalIgnoreCase, out var rest)
            && ((string[])[SignInPath, NoAccessPath, SignOutPath, DevLoginPath])
                .Any(page => rest.StartsWithSegments(page, StringComparison.OrdinalIgnoreCase));
    }

    private static string? EmailVerified(JsonElement user)
    {
        foreach (var name in (ReadOnlySpan<string>)["verified_email", "email_verified"])
        {
            if (user.TryGetProperty(name, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => value.ToString(),
                };
            }
        }

        return null;
    }

    private static IResult Challenge(string provider, string? returnUrl, HttpContext http, SignInProviders providers)
    {
        // Always ask which account, so a second person on the same computer is not signed in as the first.
        var redirectUri = LocalReturnUrl(http.Request, returnUrl);
        if (string.Equals(provider, SignInProviders.GoogleRoute, StringComparison.OrdinalIgnoreCase) && providers.Google is not null)
        {
            return Results.Challenge(
                new GoogleChallengeProperties { RedirectUri = redirectUri, Prompt = "select_account" },
                [GoogleDefaults.AuthenticationScheme]);
        }

        if (string.Equals(provider, SignInProviders.MicrosoftRoute, StringComparison.OrdinalIgnoreCase) && providers.Microsoft is not null)
        {
            return Results.Challenge(
                new MicrosoftChallengeProperties { RedirectUri = redirectUri, Prompt = "select_account" },
                [MicrosoftAccountDefaults.AuthenticationScheme]);
        }

        return Results.NotFound();
    }

    private static async Task<IResult> SignOutAsync(HttpContext http, IAntiforgery antiforgery)
    {
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.BadRequest();
        }

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect(AppPath(http.Request, SignInPath));
    }

    /// <summary>Local testing without OAuth clients: signs in as any e-mail on the access list.</summary>
    private static async Task<IResult> DevLoginAsync(HttpContext http, IAntiforgery antiforgery, AccessList access)
    {
        // Meant for a browser on the developer's own computer talking to the app directly: a request from
        // another address, through a proxy, or for another host name is refused. A best effort for a
        // Development-only route, not a security boundary.
        if (!HttpMethods.IsPost(http.Request.Method)
            || http.Connection.RemoteIpAddress is not { } remote
            || !IPAddress.IsLoopback(remote)
            || http.Request.Headers.ContainsKey("X-Forwarded-For")
            || http.Request.Headers.ContainsKey("X-Original-For")
            || !IsLocalHost(http.Request.Host.Host))
        {
            return BareNotFound(http);
        }

        if (!http.Request.HasFormContentType || !await antiforgery.IsRequestValidAsync(http))
        {
            return Results.BadRequest();
        }

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var email = form["email"].ToString();
        if (!access.Allows(email))
        {
            return Results.Redirect(AppPath(http.Request, NoAccessPath));
        }

        var address = email.Trim();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, address), new Claim(ClaimTypes.Name, address)],
            authenticationType: "dev-login");
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
        return Results.Redirect(LocalReturnUrl(http.Request, form["returnUrl"]));
    }

    /// <summary>
    /// Pages and media (<c>/fotos/{id}/...</c>) send an anonymous visitor to <c>/entrar</c>; only
    /// <c>/api</c> answers 401, since a script cannot follow a sign-in page (D-048).
    /// </summary>
    private static Task RedirectToSignInAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsApiPath(context.Request))
        {
            // 401 must name a scheme; the cookie is not a standard one, so the realm says where to sign in.
            context.Response.Headers.WWWAuthenticate = $"Cookie realm=\"sepiabox\", sign-in=\"{AppPath(context.Request, SignInPath)}\"";
            Bare(context.HttpContext, StatusCodes.Status401Unauthorized);
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Someone signed in but not on the list: a page shows <c>/sem-acesso</c>; anything else that is not a
    /// page (media, <c>/api</c>) answers 403, so it never reaches storage or a sign-in loop (D-048).
    /// </summary>
    private static Task RedirectToNoAccessAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        var isPage = endpoint is null || endpoint.Metadata.GetMetadata<ComponentTypeMetadata>() is not null;
        if (isPage && !IsApiPath(context.Request))
        {
            context.Response.Redirect(context.RedirectUri);
        }
        else
        {
            Bare(context.HttpContext, StatusCodes.Status403Forbidden);
        }

        return Task.CompletedTask;
    }

    private static bool IsLocalHost(string host) =>
        host is "localhost" or "127.0.0.1" or "[::1]" or "::1";

    // Status code pages would re-execute a POST as a POST to the /not-found page, which then fails its own
    // antiforgery check with a 400; an empty body with the 404 already set avoids that.
    private static IResult BareNotFound(HttpContext http)
    {
        Bare(http, StatusCodes.Status404NotFound);
        return Results.Empty;
    }

    private static bool IsApiPath(HttpRequest request) => request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    // A status for a script or an <img>, not the HTML "page not found" that status code pages would render.
    private static void Bare(HttpContext http, int statusCode)
    {
        http.Response.StatusCode = statusCode;
        if (http.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
        {
            statusCodePages.Enabled = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Key} is empty: nobody can sign in.")]
    private static partial void LogEmptyAccessList(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No OAuth client is configured (Auth:Google, Auth:Microsoft): nobody can sign in.")]
    private static partial void LogNoProviders(ILogger logger);
}
