using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Localization;

namespace Sepiabox.Web.Tests;

/// <summary>Which culture a request gets (D-050): the cookie, then Accept-Language, then English.</summary>
public sealed class CultureSelectionTests
{
    [Theory]
    [InlineData("pt", "pt-BR")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("PT-br", "pt-BR")]
    [InlineData("pt-AO", "pt-BR")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("de-DE", null)]
    [InlineData("ptx", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Maps_a_language_tag_to_a_supported_culture(string? tag, string? expected)
    {
        Assert.Equal(expected, WebLocalization.ForLanguageTag(tag));
    }

    [Theory]
    [InlineData("en-US", null, "en")]
    [InlineData("pt-PT", null, "pt-BR")]
    [InlineData("pt-BR", null, "pt-BR")]
    [InlineData("pt", null, "pt-BR")]
    [InlineData("de-DE", null, "en")]
    [InlineData(null, null, "en")]
    [InlineData("de-DE,pt;q=0.8,en;q=0.5", null, "pt-BR")]
    [InlineData("de-DE,en;q=0.9,pt;q=0.8", null, "en")]
    [InlineData("en;q=0.4,pt-PT;q=0.9", null, "pt-BR")]
    [InlineData("pt;q=0,en", null, "en")]
    [InlineData("pt-BR", "c=en|uic=en", "en")]
    [InlineData("en-US", "c=pt-BR|uic=pt-BR", "pt-BR")]
    [InlineData("pt-BR", "c=de-DE|uic=de-DE", "pt-BR")]
    [InlineData("en-US", "nonsense", "en")]
    public async Task Chooses_the_cookie_then_the_browser_language_then_english(string? acceptLanguage, string? cookie, string expected)
    {
        var chosen = await CultureForAsync(acceptLanguage, cookie);

        Assert.Equal(expected, chosen.UICulture.Name);
        Assert.Equal(expected, chosen.Culture.Name);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("EN")]
    [InlineData("pt-BR")]
    public void Accepts_the_supported_cultures_in_any_casing(string culture)
    {
        Assert.NotNull(WebLocalization.Supported(culture));
    }

    [Theory]
    [InlineData("pt")]
    [InlineData("pt-PT")]
    [InlineData("de")]
    [InlineData("")]
    [InlineData(null)]
    public void Refuses_a_culture_the_app_does_not_have(string? culture)
    {
        Assert.Null(WebLocalization.Supported(culture));
    }

    // Runs the real request localization middleware with the app's options and reports what it chose.
    private static async Task<RequestCulture> CultureForAsync(string? acceptLanguage, string? cookie)
    {
        var services = new ServiceCollection().AddLogging().AddSepiaboxLocalization().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        RequestCulture? chosen = null;
        app.UseRequestLocalization();
        app.Run(http =>
        {
            chosen = http.Features.Get<IRequestCultureFeature>()!.RequestCulture;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext { RequestServices = services };
        if (acceptLanguage is not null)
        {
            context.Request.Headers.AcceptLanguage = acceptLanguage;
        }

        if (cookie is not null)
        {
            context.Request.Headers.Cookie = $"{CookieRequestCultureProvider.DefaultCookieName}={Uri.EscapeDataString(cookie)}";
        }

        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            await app.Build()(context);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }

        return chosen!;
    }
}
