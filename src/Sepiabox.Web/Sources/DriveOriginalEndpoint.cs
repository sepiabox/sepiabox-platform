namespace Sepiabox.Web.Sources;

/// <summary>
/// <c>/photos/{id}/sources/{sourceId}/original</c> (S-038): the original of a Google Drive source, read from the Drive and passed on, never
/// kept. Answers only to a signed-in person on the access list, like everything else; the Drive file is found from the source the page
/// listed, never from an address the visitor wrote.
/// </summary>
public static class DriveOriginalEndpoint
{
    public const string PathTemplate = "/photos/{id}/sources/{sourceId:long}/original";

    public static string Path(string assetId, long sourceId) => $"photos/{assetId}/sources/{sourceId}/original";

    public static IEndpointRouteBuilder MapDriveOriginalEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(PathTemplate, OpenAsync);
        return endpoints;
    }

    public static async Task<IResult> OpenAsync(string id, long sourceId, HttpContext http, IDriveOriginals originals)
    {
        if (!Sepiabox.Web.Services.AssetIds.IsValid(id))
        {
            return Results.NotFound();
        }

        var (outcome, file) = await originals.OpenAsync(id, sourceId, http.RequestAborted);
        switch (outcome)
        {
            case DriveOriginalOutcome.Opened when file is not null:
                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.RegisterForDisposeAsync(file);
                return Results.File(file.Content, file.ContentType, file.Name, enableRangeProcessing: false);
            case DriveOriginalOutcome.Reconnect:
                // The account's token is no good: the sources page says so and offers to connect again.
                return Results.Redirect("/sources?" + SourceEndpoints.NoteParameter + "=reconnect");
            case DriveOriginalOutcome.Failed:
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            default:
                return Results.NotFound();
        }
    }
}
