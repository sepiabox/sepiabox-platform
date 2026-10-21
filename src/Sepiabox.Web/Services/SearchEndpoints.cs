using System.Text;
using System.Text.Json.Serialization;
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

    /// <summary>A count (S-026): the total, the largest groups, and the undated assets a time filter leaves out.</summary>
    public sealed record CountBody(int Total, IReadOnlyList<CountGroup> Groups, int? Undated);

    /// <summary>
    /// The items found, or for a query with <c>aggregate</c> its <see cref="Count"/> (and no items); a count with a
    /// description is not counted (D-075): <see cref="NotCounted"/> says why, and the items are its results instead.
    /// </summary>
    public sealed record Response(
        IReadOnlyList<Item> Items,
        bool HasMore,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CountBody? Count = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NotCounted = null);

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

        var json = await ReadBoundedAsync(http.Request.Body, MaxBodyBytes, http.RequestAborted);
        if (json is null)
        {
            return Invalid($"The search is larger than {MaxBodyBytes} bytes.");
        }

        if (!SearchQueryJson.TryParse(json, out var query, out var error))
        {
            return Invalid(error);
        }

        Response response;
        try
        {
            response = await AnswerAsync(query, library, text, http.RequestAborted);
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

        return Results.Ok(response);
    }

    /// <summary>
    /// What a query answers: its items, or its count when it has <c>aggregate</c> (S-026); a count with a description
    /// gives the items with the reason it is not counted. Shared with <c>/api/questions</c>.
    /// </summary>
    public static async Task<Response> AnswerAsync(
        SearchQuery query, ILibraryQueries library, IStringLocalizer<SharedResource> text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(text);
        if (query.Aggregate is not null && await library.CountAsync(query, cancellationToken) is { } count)
        {
            return new Response([], false, new CountBody(count.Total, count.Groups, count.Undated));
        }

        var page = await library.SearchAsync(query, cancellationToken);
        return new Response(
            [.. page.Items.Select(i => new Item(i.Id, i.Kind, i.CapturedAt, i.DurationSeconds, i.HasThumbnail))],
            page.HasMore,
            NotCounted: query.Aggregate is null ? null : text["Count_NeedsDescription"].Value);
    }

    private static IResult Invalid(string message) =>
        Results.Problem(title: "Invalid search", detail: message, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// The body as UTF-8 text, or null when it is longer than <paramref name="maxBytes"/>: read in chunks, so a
    /// chunked body (which has no Content-Length to check first) is never read past the limit.
    /// </summary>
    internal static async Task<string?> ReadBoundedAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
