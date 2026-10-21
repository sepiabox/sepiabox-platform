using System.Text.Json;
using Microsoft.Extensions.Localization;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;

namespace Sepiabox.Web.Services;

/// <summary>
/// <c>POST /api/questions</c> (S-025): a question in, the DSL it was understood as and the matching assets out. Behind
/// the sign-in and access list like every route (D-048, D-056), JSON only (as <c>/api/search</c>, D-069), and counted
/// against the person's daily limit. Every failure is a problem response with a message in the visitor's language.
/// </summary>
public static class QuestionEndpoints
{
    /// <summary>A question is at most <see cref="QuestionPrompt.MaxQuestionLength"/> characters; this leaves room for JSON.</summary>
    public const int MaxBodyBytes = 8 * 1024;

    public sealed record Request(string? Question);

    public sealed record Response(JsonElement Query, IReadOnlyList<SearchEndpoints.Item> Items, bool HasMore);

    public static IEndpointRouteBuilder MapQuestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/questions", AskAsync);
        return endpoints;
    }

    public static async Task<IResult> AskAsync(
        HttpContext http, QuestionService questions, ILibraryQueries library, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(text);
        if (!http.Request.HasJsonContentType())
        {
            return Results.Problem(
                title: "Unsupported content type", detail: "Send the question as application/json.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return Problem(text["Ask_Invalid", QuestionPrompt.MaxQuestionLength], StatusCodes.Status400BadRequest);
        }

        // Bounded whatever the headers say: a chunked body has no Content-Length.
        var json = await SearchEndpoints.ReadBoundedAsync(http.Request.Body, MaxBodyBytes, http.RequestAborted);
        Request? request;
        try
        {
            request = json is null ? null : JsonSerializer.Deserialize<Request>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null)
        {
            return Problem(text["Ask_Invalid", QuestionPrompt.MaxQuestionLength], StatusCodes.Status400BadRequest);
        }

        var result = await questions.AskAsync(request.Question, http.User, http.RequestAborted);
        switch (result.Outcome)
        {
            case AskOutcome.Answered:
                break;
            case AskOutcome.Invalid:
                return Problem(text["Ask_Invalid", QuestionPrompt.MaxQuestionLength], StatusCodes.Status400BadRequest);
            case AskOutcome.NotConfigured:
                return Problem(text["Ask_NotConfigured"], StatusCodes.Status503ServiceUnavailable);
            case AskOutcome.LimitReached:
                return Problem(text["Ask_LimitReached", questions.DailyLimit], StatusCodes.Status429TooManyRequests);
            case AskOutcome.NotUnderstood:
                return Problem(text["Ask_NotUnderstood"], StatusCodes.Status422UnprocessableEntity);
            case AskOutcome.Unavailable:
                http.Response.Headers.RetryAfter = "30";
                return Problem(text["Ask_Unavailable"], StatusCodes.Status503ServiceUnavailable);
            default:
                return Problem(text["Ask_Failed"], StatusCodes.Status502BadGateway);
        }

        var query = result.Query!;
        JsonElement dsl;
        using (var document = JsonDocument.Parse(SearchQueryJson.Serialize(query)))
        {
            dsl = document.RootElement.Clone();
        }

        if (query.Filters.IsEmpty)
        {
            return Problem(text["Ask_NothingToFilter"], StatusCodes.Status422UnprocessableEntity, dsl);
        }

        SearchPage page;
        try
        {
            page = await library.SearchAsync(query, http.RequestAborted);
        }
        catch (VisualSearchUnavailableException)
        {
            // Understood, but the text model is missing (S-056): the query is returned so the caller can drop visual.
            http.Response.Headers.RetryAfter = "3600";
            return Problem(text["Search_DescribeUnavailable"], StatusCodes.Status503ServiceUnavailable, dsl, "filters.visual");
        }

        return Results.Ok(new Response(
            dsl, [.. page.Items.Select(i => new SearchEndpoints.Item(i.Id, i.Kind, i.CapturedAt, i.DurationSeconds, i.HasThumbnail))], page.HasMore));
    }

    private static IResult Problem(string detail, int status, JsonElement? query = null, string? field = null)
    {
        var extensions = new Dictionary<string, object?>();
        if (query is { } q)
        {
            extensions["query"] = q;
        }

        if (field is not null)
        {
            extensions["field"] = field;
        }

        return Results.Problem(detail: detail, statusCode: status, extensions: extensions);
    }
}
