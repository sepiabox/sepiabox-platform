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
}
