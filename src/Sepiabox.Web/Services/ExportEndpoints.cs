namespace Sepiabox.Web.Services;

/// <summary>The download of a finished export (S-040): a redirect to a one-hour link, signed when asked, never into a page.</summary>
public static class ExportEndpoints
{
    public const string DownloadPath = "/settings/export/{id:long}/download";

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(DownloadPath, DownloadAsync);
        return endpoints;
    }

    public static async Task<IResult> DownloadAsync(long id, HttpContext http, IExportRequests exports)
    {
        var link = await exports.DownloadAsync(id, http.RequestAborted);
        if (link is null)
        {
            return Results.NotFound();
        }

        // The link is private to whoever asked: not for a shared cache.
        http.Response.Headers.CacheControl = "no-store";
        return Results.Redirect(link.AbsoluteUri);
    }
}
