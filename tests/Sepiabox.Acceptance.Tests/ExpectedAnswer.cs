using System.Globalization;
using System.Text.Json.Nodes;
using Sepiabox.Catalog.Search;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Acceptance.Tests;

/// <summary>
/// The numbers an answer must show (S-054): its total, its groups when grouped (a null key is the "no date" or "no
/// place" group), and, for a time-filtered count, how many items have no date. <see cref="Source"/> says where they
/// come from, for the failure message.
/// </summary>
public sealed record ExpectedAnswer(string Source, int Total, IReadOnlyDictionary<string, int>? Groups = null, int? Undated = null)
{
    /// <summary>The key of the group with no value (no date, no place) in <see cref="Groups"/>.</summary>
    public const string None = "(none)";

    /// <summary>Every difference between this and <paramref name="answer"/>, each with both numbers.</summary>
    public IEnumerable<string> Differences(CountResult answer)
    {
        if (answer.Total != Total)
        {
            yield return $"total: expected {Total} ({Source}), answered {answer.Total}";
        }

        if (Groups is not null)
        {
            var answered = answer.Groups.ToDictionary(g => g.Key ?? None, g => g.Count);
            foreach (var key in Groups.Keys.Union(answered.Keys).Order(StringComparer.Ordinal))
            {
                var expected = Groups.GetValueOrDefault(key);
                var got = answered.GetValueOrDefault(key);
                if (expected != got)
                {
                    yield return $"group {key}: expected {expected} ({Source}), answered {got}";
                }
            }

            if (answer.AllGroups != answer.Groups.Count)
            {
                yield return $"groups: answered {answer.Groups.Count} of {answer.AllGroups}, expected all {Groups.Count}";
            }
        }

        if (Undated is not null && answer.Undated != Undated)
        {
            yield return $"no date: expected {Undated} ({Source}), answered {answer.Undated?.ToString(CultureInfo.InvariantCulture) ?? "nothing"}";
        }
    }

    /// <summary>
    /// The reference for an <c>expect</c> name of <c>acceptance.questions.json</c>: years, kinds and the undated come
    /// from <c>report --json</c>, cities from the generator (the report has no places).
    /// </summary>
    internal static ExpectedAnswer For(string expect, JsonNode report, FixtureExpectations generated)
    {
        var years = report["years"]!.AsArray().Select(y => (
            Year: y!["year"]!.GetValue<int>(), Photos: y["photos"]!.GetValue<int>(), Videos: y["videos"]!.GetValue<int>())).ToList();
        var undated = report["undated"]!;
        int Undated(string kind) => undated[kind]!.GetValue<int>();
        var total = report["total"]!.GetValue<int>();

        ExpectedAnswer PerYear(string kind, Func<(int Year, int Photos, int Videos), int> count)
        {
            var groups = years.Where(y => count(y) > 0).ToDictionary(y => y.Year.ToString(CultureInfo.InvariantCulture), count);
            if (Undated(kind) > 0)
            {
                groups[None] = Undated(kind);
            }

            return new("report", groups.Values.Sum(), groups);
        }

        switch (expect)
        {
            case "photosByYear":
                return PerYear("photos", y => y.Photos);
            case "videosByYear":
                return PerYear("videos", y => y.Videos);
            case "undated":
                return new("report", Undated("total"));
            case "photos1901":
                // "years" is a time filter, so the answer also says how many photos have no date.
                return new("report", years.Where(y => y.Year == 1901).Sum(y => y.Photos), Undated: Undated("photos"));
            case "videosInLisbon":
                return new("generator", generated.Assets.Count(a => a.Kind == MediaKind.Video && a.City == FixtureLibrary.Lisbon));
            case "itemsByCity":
                {
                    var groups = generated.ByCity.ToDictionary(c => c.Key, c => c.Value);
                    groups[None] = generated.Assets.Count - groups.Values.Sum();
                    return new("generator", generated.Assets.Count, groups);
                }

            case "byKind":
                return new("report", total, new Dictionary<string, int>
                {
                    ["photo"] = years.Sum(y => y.Photos) + Undated("photos"),
                    ["video"] = years.Sum(y => y.Videos) + Undated("videos"),
                });
            case "total":
                return new("report", total);
            default:
                throw new InvalidOperationException($"Unknown expect '{expect}' in {AcceptanceQuestions.FileName}.");
        }
    }
}
