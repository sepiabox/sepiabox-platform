namespace Sepiabox.Web.Services;

/// <summary>Thumbnails taken in one month, or the assets without a date.</summary>
public sealed record TimelineGroup(string Title, IReadOnlyList<TimelineItem> Items);

public static class TimelineGroups
{
    public const string NoDateTitle = "Sem data";

    /// <summary>
    /// Groups items by month, newest month first, with the undated ones in a last group. The query already
    /// sorts this way; grouping again here keeps the page correct whatever order it is given.
    /// </summary>
    public static IReadOnlyList<TimelineGroup> ByMonth(IEnumerable<TimelineItem> items)
    {
        var list = items.ToList();
        var dated = list
            .Where(i => i.CapturedAt is not null)
            .GroupBy(i => (i.CapturedAt!.Value.Year, i.CapturedAt!.Value.Month))
            .OrderByDescending(g => g.Key.Year)
            .ThenByDescending(g => g.Key.Month)
            .Select(g => new TimelineGroup(
                PortugueseDates.MonthAndYear(g.Key.Year, g.Key.Month),
                [.. g.OrderByDescending(i => i.CapturedAt)]));

        var undated = list.Where(i => i.CapturedAt is null).ToList();
        return undated.Count == 0 ? [.. dated] : [.. dated, new TimelineGroup(NoDateTitle, undated)];
    }
}
