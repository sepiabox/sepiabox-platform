using Microsoft.Extensions.Localization;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

/// <summary>What the page and the APIs say when a search names people the catalog does not have (S-021).</summary>
public static class PeopleSearch
{
    /// <summary>The message, in the visitor's language: the missing names, and "This is me" when it is missing.</summary>
    public static string Message(UnknownPeopleException unknown, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(unknown);
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<string>();
        if (unknown.Missing.Count > 0)
        {
            parts.Add(text["Search_UnknownPeople", string.Join(", ", unknown.Missing)]);
        }

        if (unknown.MeMissing)
        {
            parts.Add(text["Search_MeNotLinked"]);
        }

        return string.Join(" ", parts);
    }

    /// <summary>A 400 naming what is missing: <c>missing</c> (the names) and <c>me</c> (true when "This is me" is).</summary>
    public static IResult Problem(UnknownPeopleException unknown, IStringLocalizer<SharedResource> text, object? query = null)
    {
        ArgumentNullException.ThrowIfNull(unknown);
        var extensions = new Dictionary<string, object?>
        {
            ["field"] = "filters.people",
            ["missing"] = unknown.Missing,
            ["me"] = unknown.MeMissing,
        };
        if (query is not null)
        {
            extensions["query"] = query;
        }

        return Results.Problem(title: "Unknown people", detail: Message(unknown, text), statusCode: StatusCodes.Status400BadRequest, extensions: extensions);
    }
}
