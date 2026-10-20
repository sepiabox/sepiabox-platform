using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Localization;

/// <summary>
/// The UI language (D-050): English by default, Brazilian Portuguese as the first translation. The culture
/// cookie, written by the language switch, wins; otherwise the browser's <c>Accept-Language</c> decides.
/// </summary>
public static class WebLocalization
{
    public const string English = "en";
    public const string BrazilianPortuguese = "pt-BR";

    /// <summary>The language switch posts here (anonymous, like the sign-in routes, D-048).</summary>
    public const string SwitchPath = "/language";

    public const string ResourcesPath = "Resources";

    /// <summary>How long the browser remembers the choice.</summary>
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(365);

    public static readonly IReadOnlyList<string> SupportedCultures = [English, BrazilianPortuguese];

    public static IServiceCollection AddSepiaboxLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = ResourcesPath);
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = SupportedCultures.Select(CultureInfo.GetCultureInfo).ToList();
            options.DefaultRequestCulture = new RequestCulture(English);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            // Not the default query string provider: a link must not change the language.
            options.RequestCultureProviders =
            [
                new OneCultureCookieProvider(),
                new BrowserLanguageProvider(),
            ];
            options.ApplyCurrentCultureToResponseHeaders = true;
        });
        return services;
    }

    public static IEndpointRouteBuilder MapLanguageSwitch(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(SwitchPath, SwitchAsync)
            .AllowAnonymous()
            // Checked by hand below, as for sign-out, so a bad token is an empty 400 and never a page.
            .DisableAntiforgery();
        return endpoints;
    }

    /// <summary>The supported culture for a requested name, or null when it is not one the app has.</summary>
    public static string? Supported(string? culture) =>
        SupportedCultures.FirstOrDefault(c => string.Equals(c, culture, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The culture for one language tag of <c>Accept-Language</c>: any Portuguese is Brazilian Portuguese,
    /// any English is English, anything else is not one the app has.
    /// </summary>
    public static string? ForLanguageTag(string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? null
        : IsLanguage(tag, "pt") ? BrazilianPortuguese
        : IsLanguage(tag, "en") ? English
        : null;

    private static bool IsLanguage(string tag, string language) =>
        tag.Equals(language, StringComparison.OrdinalIgnoreCase)
        || tag.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase);

    private static async Task<IResult> SwitchAsync(HttpContext http, IAntiforgery antiforgery)
    {
        if (!http.Request.HasFormContentType || !await antiforgery.IsRequestValidAsync(http))
        {
            return AuthSetup.BareStatus(http, StatusCodes.Status400BadRequest);
        }

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        if (Supported(form["culture"]) is not { } culture)
        {
            return AuthSetup.BareStatus(http, StatusCodes.Status400BadRequest);
        }

        http.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Path = http.Request.PathBase.HasValue ? http.Request.PathBase.Value : "/",
                MaxAge = CookieLifetime,
                HttpOnly = true,
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                // Only a preference the visitor chose; it identifies no one.
                IsEssential = true,
            });
        return Results.Redirect(AuthSetup.LocalUrl(http.Request, form["returnUrl"]));
    }
}

/// <summary>
/// The standard culture cookie, read as one culture: a hand-made <c>c=en|uic=pt-BR</c> gives <c>pt-BR</c> for
/// both, since the UI culture picks the text and the culture would pick the month names.
/// </summary>
internal sealed class OneCultureCookieProvider : CookieRequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext) =>
        await base.DetermineProviderCultureResult(httpContext) is { UICultures: [var uiCulture, ..] }
            ? new ProviderCultureResult(uiCulture)
            : null;
}

/// <summary>
/// The browser's languages in its order of preference: the first Portuguese or English one decides, and a
/// browser with neither gets the default, English (D-050).
/// </summary>
internal sealed class BrowserLanguageProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var culture = httpContext.Request.GetTypedHeaders().AcceptLanguage
            .Where(language => language.Quality is not 0)
            .OrderByDescending(language => language.Quality ?? 1)
            .Select(language => WebLocalization.ForLanguageTag(language.Value.Value))
            .FirstOrDefault(culture => culture is not null);
        return Task.FromResult(culture is null ? null : new ProviderCultureResult(culture));
    }
}
