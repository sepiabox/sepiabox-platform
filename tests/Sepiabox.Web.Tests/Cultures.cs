using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sepiabox.Web.Localization;

namespace Sepiabox.Web.Tests;

/// <summary>Runs part of a test in one culture, as the request localization middleware would set it.</summary>
internal static class Cultures
{
    public const string English = WebLocalization.English;
    public const string BrazilianPortuguese = WebLocalization.BrazilianPortuguese;

    public static IDisposable Use(string name)
    {
        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        return new Restore(culture, uiCulture);
    }

    /// <summary>The app's UI text, read from its resources as the web reads them.</summary>
    public static IStringLocalizer<SharedResource> Text() =>
        new StringLocalizer<SharedResource>(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions { ResourcesPath = WebLocalization.ResourcesPath }),
            NullLoggerFactory.Instance));

    private sealed class Restore(CultureInfo culture, CultureInfo uiCulture) : IDisposable
    {
        public void Dispose()
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}

/// <summary>
/// A bUnit context with the app's localization, rendering in English unless a test switches culture with
/// <see cref="Cultures.Use"/>; the machine's own culture never leaks into a test.
/// </summary>
public abstract class LocalizedContext : BunitContext
{
    private readonly IDisposable _culture;

    protected LocalizedContext()
    {
        _culture = Cultures.Use(Cultures.English);
        Services.AddSepiaboxLocalization();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _culture.Dispose();
        }

        base.Dispose(disposing);
    }
}
