namespace Sepiabox.Web.Services;

/// <summary>Results under one occasion, or under none (S-041).</summary>
public sealed record OccasionGroup(OccasionSummary? Occasion, IReadOnlyList<TimelineItem> Items);

/// <summary>"Photos like this part of a photo", grouped by occasion (S-041, D-099).</summary>
public static class OccasionGroups
{
    /// <summary>
    /// <paramref name="items"/>, most similar first, in groups by occasion: the group with the most similar item first,
    /// each group in the order given. Items in no occasion (undated ones, or before the occasions were rebuilt, D-092) are
    /// the last group.
    /// </summary>
    public static IReadOnlyList<OccasionGroup> Group(IReadOnlyList<TimelineItem> items, IReadOnlyDictionary<string, OccasionSummary> occasionOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(occasionOf);
        var groups = new List<OccasionGroup>();
        var byOccasion = new Dictionary<long, List<TimelineItem>>();
        var none = new List<TimelineItem>();
        foreach (var item in items)
        {
            if (!occasionOf.TryGetValue(item.Id, out var occasion))
            {
                none.Add(item);
            }
            else if (byOccasion.TryGetValue(occasion.Id, out var members))
            {
                members.Add(item);
            }
            else
            {
                members = [item];
                byOccasion[occasion.Id] = members;
                groups.Add(new OccasionGroup(occasion, members));
            }
        }

        if (none.Count > 0)
        {
            groups.Add(new OccasionGroup(null, none));
        }

        return groups;
    }
}
