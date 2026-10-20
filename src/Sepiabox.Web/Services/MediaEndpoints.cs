using Azure;
using Azure.Identity;

namespace Sepiabox.Web.Services;

/// <summary>
/// The only way the browser reaches blobs: each request is answered with a redirect to a link signed at
/// that moment, so pages never carry links that expire while they are open (D-039). The access list
/// protects them like any page: an anonymous request is sent to <c>/entrar</c> and someone not on the
/// list gets 403, before any link is signed (D-048).
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
            var url = derivative == DerivativeNames.Proxy
                ? await links.ForVideoAsync(blobPath, http.RequestAborted)
                : await links.ForDerivativeAsync(blobPath, http.RequestAborted);
            // The link is stable for the rest of its window, so the browser may reuse it (and the image or video).
            http.Response.Headers.CacheControl = $"private, max-age={(int)SasUrlFactory.RedirectCacheDuration.TotalSeconds}";
            return Results.Redirect(url.AbsoluteUri);
        }
        catch (Exception ex) when (IsSigningFailure(ex, http.RequestAborted))
        {
            // A page asks for up to 200 thumbnails: one short line each, not 200 stack traces.
            LogDerivativeSigningFailed(log, id, derivative, ex.GetType().Name, ex.Message);
            Unavailable(http);
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
        catch (Exception ex) when (IsSigningFailure(ex, http.RequestAborted))
        {
            LogOriginalSigningFailed(log, ex, id);
            Unavailable(http);
            return Results.Text(
                "Não foi possível preparar o download. Tente novamente daqui a pouco.",
                "text/plain; charset=utf-8",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Storage refusing or not answering the delegation key request, or the managed identity unable to get a
    /// token. Azure.Core's retry policy reports repeated failures as one AggregateException, and a timeout as a
    /// cancellation; a cancellation only counts while the browser is still waiting for this response.
    /// </summary>
    private static bool IsSigningFailure(Exception ex, CancellationToken requestAborted) => ex switch
    {
        RequestFailedException or AuthenticationFailedException => true,
        OperationCanceledException => !requestAborted.IsCancellationRequested,
        AggregateException aggregate => aggregate.Flatten().InnerExceptions is { Count: > 0 } inner
            && inner.All(e => IsSigningFailure(e, requestAborted)),
        _ => false,
    };

    // A failure is not cached (unlike the redirect), and the browser may try again in a minute.
    private static void Unavailable(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.RetryAfter = "60";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not sign a link to {Derivative} of asset {AssetId}: {ErrorType}: {ErrorMessage}")]
    private static partial void LogDerivativeSigningFailed(ILogger logger, string assetId, string derivative, string errorType, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not sign a link to the original of asset {AssetId}.")]
    private static partial void LogOriginalSigningFailed(ILogger logger, Exception exception, string assetId);
}

/// <summary>Asset ids are SHA-256 in lowercase hex (D-014); anything else cannot exist.</summary>
public static class AssetIds
{
    public static bool IsValid(string value) =>
        value.Length == 64 && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
}
