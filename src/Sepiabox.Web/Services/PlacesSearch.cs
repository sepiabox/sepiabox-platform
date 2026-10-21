using Microsoft.Extensions.Localization;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

/// <summary>What the page and the APIs say when a search names saved places the catalog does not have (S-014).</summary>
public static class PlacesSearch
{
    /// <summary>The message, in the visitor's language: the missing places, and the home when none is marked.</summary>
    public static string Message(UnknownPlacesException unknown, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(unknown);
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<string>();
        if (unknown.Missing.Count > 0)
        {
            parts.Add(text["Search_UnknownPlaces", PeopleSearch.Names(unknown.Missing, text)]);
        }

        if (unknown.HomeMissing)
        {
            parts.Add(text["Search_NoHome"]);
        }

        return string.Join(" ", parts);
    }

    /// <summary>A 400 naming what is missing: <c>missing</c> (the places) and <c>home</c> (true when no place is the home).</summary>
    public static IResult Problem(UnknownPlacesException unknown, IStringLocalizer<SharedResource> text, object? query = null)
    {
        ArgumentNullException.ThrowIfNull(unknown);
        var extensions = new Dictionary<string, object?>
        {
            ["field"] = "filters.savedPlaces",
            ["missing"] = unknown.Missing,
            ["home"] = unknown.HomeMissing,
        };
        if (query is not null)
        {
            extensions["query"] = query;
        }

        return Results.Problem(title: "Unknown places", detail: Message(unknown, text), statusCode: StatusCodes.Status400BadRequest, extensions: extensions);
    }
}
