using Microsoft.Extensions.Localization;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

/// <summary>What the pages and the API say and link for "more photos similar to this one" (S-017, D-086).</summary>
public static class SimilarSearch
{
    /// <summary>How many similar results "Show more" asks for in turn: the default, then these, up to the DSL's maximum.</summary>
    public static readonly IReadOnlyList<int> Steps = [SimilarToFilter.DefaultLimit, SearchQuery.DefaultLimit, SearchQuery.MaxLimit];

    /// <summary>"Not analysed yet" for a photo or a video by its kind, in the visitor's language.</summary>
    public static string NotAnalysed(string kind, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return AssetKinds.IsVideo(kind) ? text["Similar_NotAnalysedVideo"] : text["Similar_NotAnalysed"];
    }

    /// <summary>Why a search for similar ones could not run: the asset has no embedding yet, or is not in the catalog.</summary>
    public static string Message(AssetNotAnalysedException missing, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentNullException.ThrowIfNull(text);
        return missing.Kind is { } kind ? NotAnalysed(kind, text) : text["Similar_UnknownAsset"];
    }

    /// <summary>
    /// The same search with the next step of results (50, 200, then 1000), raising the query's own limit with it; null
    /// when it shows as many as the DSL allows, or is not a search for similar ones.
    /// </summary>
    public static SearchQuery? More(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Filters.SimilarTo is not { } similar)
        {
            return null;
        }

        var shown = query.ResultLimit;
        var next = Steps.FirstOrDefault(step => step > shown);
        return next == 0
            ? null
            : query with
            {
                Limit = Math.Max(query.Limit, next),
                Filters = query.Filters with { SimilarTo = similar with { Limit = next } },
            };
    }
}
