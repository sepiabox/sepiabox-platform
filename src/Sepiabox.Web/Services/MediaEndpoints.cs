using Azure;

namespace Sepiabox.Web.Services;

/// <summary>
/// The only way the browser reaches blobs: each request is answered with a redirect to a link signed at
/// that moment, so pages never carry links that expire while they are open (D-039). S-010's sign-in
/// protects these endpoints like any page.
/// </summary>
public static partial class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/fotos/{id}/original", OriginalAsync);
        endpoints.MapGet("/fotos/{id}/{derivative}", DerivativeAsync);
        return endpoints;
    }

    public static async Task<IResult> DerivativeAsync(
        string id, string derivative, HttpContext http, ILibraryQueries library, ISasUrlFactory links, ILogger<Program> log)
    {
        if (!AssetIds.IsValid(id) || !DerivativeNames.IsServed(derivative))
        {
            return Results.NotFound();
        }

        var blobPath = await library.GetDerivativeBlobPathAsync(id, derivative, http.RequestAborted);
        if (blobPath is null)
        {
            return Results.NotFound();
        }

        try
        {
            var url = await links.ForDerivativeAsync(blobPath, http.RequestAborted);
            // The link is stable for the rest of its window, so the browser may reuse it (and the image).
            http.Response.Headers.CacheControl = $"private, max-age={(int)SasUrlFactory.DerivativeCacheDuration.TotalSeconds}";
            return Results.Redirect(url.AbsoluteUri);
        }
        catch (RequestFailedException ex)
        {
            LogSigningFailed(log, ex, id, derivative);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    public static async Task<IResult> OriginalAsync(
        string id, HttpContext http, ILibraryQueries library, ISasUrlFactory links, ILogger<Program> log)
    {
        if (!AssetIds.IsValid(id))
        {
            return Results.NotFound();
        }

        var original = await library.GetOriginalAsync(id, http.RequestAborted);
        if (original is null)
        {
            return Results.NotFound();
        }

        try
        {
            var url = await links.ForOriginalAsync(id, original, http.RequestAborted);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Redirect(url.AbsoluteUri);
        }
        catch (RequestFailedException ex)
        {
            LogSigningFailed(log, ex, id, "original");
            return Results.Text(
                "Não foi possível preparar o download. Tente novamente daqui a pouco.",
                "text/plain; charset=utf-8",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not sign a link to {Derivative} of asset {AssetId}.")]
    private static partial void LogSigningFailed(ILogger logger, Exception exception, string assetId, string derivative);
}

/// <summary>Asset ids are SHA-256 in lowercase hex (D-014); anything else cannot exist.</summary>
public static class AssetIds
{
    public static bool IsValid(string value) =>
        value.Length == 64 && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
}
