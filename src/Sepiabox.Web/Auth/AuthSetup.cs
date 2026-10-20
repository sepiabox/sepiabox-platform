using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Diagnostics;

namespace Sepiabox.Web.Auth;

/// <summary>
/// Sign-in with Google or Microsoft, and the access list on every page and endpoint (D-006, D-048).
/// </summary>
public static partial class AuthSetup
{
    public const string SignInPath = "/entrar";
    public const string NoAccessPath = "/sem-acesso";
    public const string SignOutPath = "/sair";
    public const string DevLoginPath = "/dev-login";
    public const string CookieName = "sepiabox-auth";

    // Personal accounts only: in a work or school tenant, the tenant's administrator chooses the e-mail.
    private const string MicrosoftConsumers = "https://login.microsoftonline.com/consumers/oauth2/v2.0";

    public static WebApplicationBuilder AddSepiaboxAuth(this WebApplicationBuilder builder)
    {
        var providers = SignInProviders.From(builder.Configuration);
        builder.Services.AddSingleton(providers);
        builder.Services.AddSingleton(services => AccessList.From(services.GetRequiredService<IConfiguration>()));
        builder.Services.AddSingleton<IAuthorizationHandler, AccessListHandler>();

        var authentication = builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
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
                options.Events.OnTicketReceived = AccessTicketHandler.OnTicketReceivedAsync;
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
            });
        }

        // The same policy for [Authorize] and for everything that says nothing, which is every page and endpoint.
        var onTheList = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new AccessListRequirement())
            .Build();
        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(onTheList)
            .SetFallbackPolicy(onTheList);
        builder.Services.AddCascadingAuthenticationState();

        return builder;
    }

    public static WebApplication MapSepiaboxAuth(this WebApplication app)
    {
        var providers = app.Services.GetRequiredService<SignInProviders>();
        var devLogin = app.Environment.IsDevelopment() && !providers.Any;

        app.MapGet(SignInPath + "/{provider}", Challenge).AllowAnonymous();
        app.MapPost(SignOutPath, SignOutAsync).AllowAnonymous();

        // Outside Development, or once a real client is configured, the route exists only to answer 404 to
        // everyone; otherwise an anonymous request would be sent to /entrar like any unknown address.
        if (devLogin)
        {
            app.MapGet(DevLoginPath, DevLoginAsync).AllowAnonymous();
        }
        else
        {
            app.MapGet(DevLoginPath, () => Results.NotFound()).AllowAnonymous();
        }

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

    /// <summary>Only a path on this site, so a crafted link cannot send someone elsewhere after sign-in.</summary>
    public static string LocalReturnUrl(string? url) =>
        url is not null
        && url.StartsWith('/')
        && (url.Length == 1 || url[1] is not ('/' or '\\'))
        // Browsers drop tabs and line breaks, which would turn "/\t/host" into "//host".
        && !url.Any(char.IsControl)
            ? url
            : "/";

    private static IResult Challenge(string provider, string? returnUrl, SignInProviders providers)
    {
        // Always ask which account, so a second person on the same computer is not signed in as the first.
        var redirectUri = LocalReturnUrl(returnUrl);
        return provider switch
        {
            SignInProviders.GoogleRoute when providers.Google is not null => Results.Challenge(
                new GoogleChallengeProperties { RedirectUri = redirectUri, Prompt = "select_account" },
                [GoogleDefaults.AuthenticationScheme]),
            SignInProviders.MicrosoftRoute when providers.Microsoft is not null => Results.Challenge(
                new MicrosoftChallengeProperties { RedirectUri = redirectUri, Prompt = "select_account" },
                [MicrosoftAccountDefaults.AuthenticationScheme]),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> SignOutAsync(HttpContext http, IAntiforgery antiforgery)
    {
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.BadRequest();
        }

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.LocalRedirect("~" + SignInPath);
    }

    /// <summary>Local testing without OAuth clients: signs in as any e-mail on the access list.</summary>
    private static async Task<IResult> DevLoginAsync(string? email, string? returnUrl, HttpContext http, AccessList access)
    {
        if (!access.Allows(email))
        {
            return Results.LocalRedirect("~" + NoAccessPath);
        }

        var address = email!.Trim();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, address), new Claim(ClaimTypes.Name, address)],
            authenticationType: "dev-login");
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.LocalRedirect("~" + LocalReturnUrl(returnUrl));
    }

    /// <summary>
    /// Pages and media (<c>/fotos/{id}/...</c>) send an anonymous visitor to <c>/entrar</c>; only
    /// <c>/api</c> answers 401, since a script cannot follow a sign-in page (D-048).
    /// </summary>
    private static Task RedirectToSignInAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsApiPath(context.Request))
        {
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
