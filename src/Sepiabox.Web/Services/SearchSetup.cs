using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

public static class SearchSetup
{
    /// <summary>
    /// <c>Search:BlurryBelow</c> (S-027, D-098) is the sharpness below which a photo is blurry, 100 unless set.
    /// <c>Search:TimeZone</c>, an IANA or Windows time zone id such as <c>Europe/Lisbon</c>, is where "today" is
    /// taken for "on this day" (D-069); unset, the server's local time zone (UTC in Container Apps).
    /// </summary>
    public static SearchOptions Options(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var blurryBelow = configuration.GetValue("Search:BlurryBelow", SearchOptions.DefaultBlurryBelow);
        if (!(blurryBelow > 0))
        {
            throw new InvalidOperationException($"Search:BlurryBelow must be a number above 0, not {blurryBelow}.");
        }

        var id = configuration["Search:TimeZone"];
        if (string.IsNullOrWhiteSpace(id))
        {
            return new SearchOptions { BlurryBelow = blurryBelow };
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            ? new SearchOptions { TimeZone = zone, BlurryBelow = blurryBelow }
            : throw new InvalidOperationException($"Search:TimeZone '{id}' is not a time zone this server knows.");
    }

    /// <summary>
    /// <c>Search:WarmUp</c>: load the CLIP text model before taking requests (S-056). By default only in Production,
    /// where the image ships the model; elsewhere the first description search loads it.
    /// </summary>
    public static bool WarmUp(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        return configuration.GetValue("Search:WarmUp", environment.IsProduction());
    }

    /// <summary>
    /// <c>Search:ModelFolder</c>, where the web reads the CLIP text model and tokenizer (S-016); unset, the folder
    /// <c>sepiabox-ingest models download</c> fills on this computer (<c>SEPIABOX_MODELS</c> or the default). S-056
    /// points it at the copy in the web's image.
    /// </summary>
    public static string ModelFolder(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var folder = configuration["Search:ModelFolder"];
        return string.IsNullOrWhiteSpace(folder) ? Sepiabox.Search.Models.ModelFolder.Default() : folder;
    }
}
