namespace Sepiabox.Web.Services;

/// <summary>
/// Thumbnails taken in one month (<see cref="Month"/> is its first day), or the assets without a date
/// (<see cref="Month"/> is null). The page writes the heading in the visitor's language.
/// </summary>
public sealed record TimelineGroup(DateOnly? Month, IReadOnlyList<TimelineItem> Items);

public static class TimelineGroups
{
    /// <summary>
    /// Groups items by month, newest month first, with the undated ones in a last group. The query already
    /// sorts this way; grouping again here keeps the page correct whatever order it is given.
    /// </summary>
    public static IReadOnlyList<TimelineGroup> ByMonth(IEnumerable<TimelineItem> items)
    {
        var list = items.ToList();
        var dated = list
            .Where(i => i.CapturedAt is not null)
            .GroupBy(i => new DateOnly(i.CapturedAt!.Value.Year, i.CapturedAt!.Value.Month, 1))
            .OrderByDescending(g => g.Key)
            .Select(g => new TimelineGroup(g.Key, [.. g.OrderByDescending(i => i.CapturedAt)]));

        var undated = list.Where(i => i.CapturedAt is null).ToList();
        return undated.Count == 0 ? [.. dated] : [.. dated, new TimelineGroup(null, undated)];
    }
}
