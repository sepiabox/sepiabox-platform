using System.Text;
using Microsoft.Extensions.Localization;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

/// <summary>
/// <c>POST /api/search</c> (S-013): the search DSL v1 as JSON in, the matching assets out. Behind the same
/// sign-in and access list as every page (D-048). It changes nothing, and it only takes
/// <c>application/json</c>, which a cross-site form cannot send without the browser asking first (D-069).
/// </summary>
public static class SearchEndpoints
{
    /// <summary>Far more than any query needs; a larger body is refused before it is read whole.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    public sealed record Item(string Id, string Kind, DateTime? CapturedAt, double? DurationSeconds, bool HasThumbnail);

    public sealed record Response(IReadOnlyList<Item> Items, bool HasMore);

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/search", SearchAsync);
        return endpoints;
    }

    public static async Task<IResult> SearchAsync(HttpContext http, ILibraryQueries library, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(library);
        if (!http.Request.HasJsonContentType())
        {
            return Results.Problem(
                title: "Unsupported content type", detail: "Send the search as application/json.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return Invalid($"The search is larger than {MaxBodyBytes} bytes.");
        }

        var json = await ReadAsync(http.Request.Body, http.RequestAborted);
        if (json is null)
        {
            return Invalid($"The search is larger than {MaxBodyBytes} bytes.");
        }

        if (!SearchQueryJson.TryParse(json, out var query, out var error))
        {
            return Invalid(error);
        }

        SearchPage page;
        try
        {
            page = await library.SearchAsync(query, http.RequestAborted);
        }
        catch (VisualSearchUnavailableException)
        {
            // The text model is missing or failed to load (S-016, D-070): the rest of the search still works.
            http.Response.Headers.RetryAfter = "3600";
            // In the visitor's language (S-056), like the page; the field names what to leave out.
            return Results.Problem(
                detail: text["Search_DescribeUnavailable"],
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["field"] = "filters.visual" });
        }

        return Results.Ok(new Response(
            [.. page.Items.Select(i => new Item(i.Id, i.Kind, i.CapturedAt, i.DurationSeconds, i.HasThumbnail))], page.HasMore));
    }

    private static IResult Invalid(string message) =>
        Results.Problem(title: "Invalid search", detail: message, statusCode: StatusCodes.Status400BadRequest);

    // Null when the body is longer than the limit (a chunked body has no Content-Length to check first).
    private static async Task<string?> ReadAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
