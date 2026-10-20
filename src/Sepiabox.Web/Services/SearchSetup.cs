using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

public static class SearchSetup
{
    /// <summary>
    /// <c>Search:TimeZone</c>, an IANA or Windows time zone id such as <c>Europe/Lisbon</c>, is where "today" is
    /// taken for "on this day" (D-069); unset, the server's local time zone (UTC in Container Apps).
    /// </summary>
    public static SearchOptions Options(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var id = configuration["Search:TimeZone"];
        if (string.IsNullOrWhiteSpace(id))
        {
            return new SearchOptions();
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            ? new SearchOptions { TimeZone = zone }
            : throw new InvalidOperationException($"Search:TimeZone '{id}' is not a time zone this server knows.");
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
