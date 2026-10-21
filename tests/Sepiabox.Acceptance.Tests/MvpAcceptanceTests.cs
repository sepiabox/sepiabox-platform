using System.Globalization;
using Sepiabox.Catalog.Search;
using Sepiabox.Pipeline.Media;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Services;

namespace Sepiabox.Acceptance.Tests;

/// <summary>
/// The MVP accepted end to end (S-054), with the translator's recorded answers (CI's integration job): every
/// acceptance question, in both languages, is answered by the web's question service on the imported fixture library,
/// and its numbers must be <c>report</c>'s (years, kinds, the undated) or the generator's (cities). The same questions
/// with the real model are <see cref="MvpAcceptanceLiveTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MvpAcceptanceTests(MvpWorld world) : IClassFixture<MvpWorld>
{
    private static readonly IReadOnlyList<AcceptanceQuestion> Questions = AcceptanceQuestions.Load();

    [Fact]
    public async Task Answers_every_question_with_the_numbers_of_report_and_the_generator_when_replayed()
    {
        var recorded = AcceptanceQuestions.Recorded();
        var misses = new List<string>();
        foreach (var question in Questions)
        {
            var expected = ExpectedAnswer.For(question.Expect, world.Report, world.Expected);
            foreach (var (key, text) in question.InBothLanguages())
            {
                Assert.True(recorded.TryGetValue(key, out var response), $"No recorded response for {key}.");
                using var translator = RecordedTranslator.Replaying(response!);
                misses.AddRange(await MvpAnswers.CheckAsync(world, key, text, translator, expected));
            }
        }

        Assert.True(misses.Count == 0, "Answers that differ:\n" + string.Join('\n', misses));
    }

    [Fact]
    public void Imports_every_asset_without_problems_when_the_drive_is_uploaded()
    {
        Assert.Empty(world.Import.Problems);
        Assert.Equal(world.Expected.Assets.Count, world.Import.Imported);
    }

    [Fact]
    public void Agrees_with_the_generator_when_report_counts_the_work_folder()
    {
        var generated = world.Expected;
        var years = world.Report["years"]!.AsArray().ToDictionary(y => y!["year"]!.GetValue<int>());
        Assert.Equal(generated.Assets.Count, world.Report["total"]!.GetValue<int>());
        Assert.Equal(generated.ByYearAndKind.Select(e => e.Key.Year).Distinct().Order(), years.Keys.Order());
        foreach (var ((year, kind), count) in generated.ByYearAndKind)
        {
            Assert.Equal(count, years[year]![kind == MediaKind.Photo ? "photos" : "videos"]!.GetValue<int>());
        }

        Assert.Equal(generated.UndatedOf(MediaKind.Photo), world.Report["undated"]!["photos"]!.GetValue<int>());
        Assert.Equal(generated.UndatedOf(MediaKind.Video), world.Report["undated"]!["videos"]!.GetValue<int>());
        Assert.Equal(generated.Undated, world.Report["undated"]!["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Counts_the_duplicate_once_when_two_paths_hold_the_same_file()
    {
        var generated = world.Expected;
        Assert.True(generated.Duplicates > 0, "The fixture library has no exact duplicate.");

        var total = await AnswerAsync("total/pt");

        Assert.Equal(generated.MediaFiles - generated.Duplicates, world.Report["total"]!.GetValue<int>());
        Assert.True(
            total.Total == generated.MediaFiles - generated.Duplicates,
            $"\"total\": answered {total.Total}, expected {generated.MediaFiles - generated.Duplicates} unique assets ({generated.MediaFiles} media files, {generated.Duplicates} duplicate).");
    }

    [Fact]
    public async Task Counts_folder_dated_files_in_their_year_when_only_the_folder_has_a_date()
    {
        var expected = world.Expected.Count(1901, MediaKind.Photo);
        Assert.True(expected > 0, $"The fixture library has no photo dated by the folder {FixtureLibrary.FarPastFolder}.");

        var photos = await AnswerAsync("photos-1901/en");

        Assert.True(photos.Total == expected, $"\"photos-1901\": answered {photos.Total}, expected {expected} (the folder-dated photos).");
    }

    [Fact]
    public async Task Says_how_many_items_have_no_date_as_report_does_when_the_question_has_a_time_filter()
    {
        var undated = world.Report["undated"]!;
        Assert.True(undated["total"]!.GetValue<int>() > 0, "The fixture library has no undated file.");

        var photos = await AnswerAsync("photos-1901/pt");
        var everything = await world.CountAsync("""{"filters":{"years":[1901]},"aggregate":{"count":"assets"}}""");

        Assert.True(
            photos.Undated == undated["photos"]!.GetValue<int>(),
            $"\"photos-1901\": answered {photos.Undated} photos with no date, report says {undated["photos"]}.");
        Assert.True(
            everything.Undated == undated["total"]!.GetValue<int>(),
            $"Items of 1901: answered {everything.Undated} items with no date, report says {undated["total"]}.");
    }

    [Fact]
    public async Task Counts_videos_with_locations_in_their_city()
    {
        var expected = new Dictionary<string, int>
        {
            [FixtureLibrary.Lisbon] = world.Expected.Assets.Count(a => a.Kind == MediaKind.Video && a.City == FixtureLibrary.Lisbon),
            [FixtureLibrary.Porto] = world.Expected.Assets.Count(a => a.Kind == MediaKind.Video && a.City == FixtureLibrary.Porto),
        };
        Assert.All(expected, e => Assert.True(e.Value > 0, $"The fixture library has no video in {e.Key}."));

        var lisbon = await AnswerAsync("videos-lisbon/pt");
        var byCity = await world.CountAsync("""{"filters":{"kinds":["video"]},"aggregate":{"count":"assets","groupBy":"city"}}""");

        Assert.True(lisbon.Total == expected[FixtureLibrary.Lisbon], $"\"videos-lisbon\": answered {lisbon.Total}, the generator has {expected[FixtureLibrary.Lisbon]}.");
        foreach (var (city, count) in expected)
        {
            var answered = byCity.Groups.SingleOrDefault(g => g.Key == city)?.Count ?? 0;
            Assert.True(answered == count, $"Videos in {city}: answered {answered}, the generator has {count}.");
        }
    }

    private async Task<CountResult> AnswerAsync(string key)
    {
        var id = key[..key.IndexOf('/', StringComparison.Ordinal)];
        var question = Questions.Single(q => q.Id == id);
        var text = question.InBothLanguages().Single(q => q.Key == key).Text;
        using var translator = RecordedTranslator.Replaying(AcceptanceQuestions.Recorded()[key]);
        var (asked, count) = await world.AskAsync(text, translator);
        Assert.True(asked.Outcome == AskOutcome.Answered && count is not null, $"{key} \"{text}\": {asked.Outcome}, not a count.");
        return count!;
    }
}

/// <summary>One question asked and compared, shared by the recorded and the live runs.</summary>
internal static class MvpAnswers
{
    /// <summary>What is wrong with the answer to <paramref name="text"/>: nothing, or one line per difference.</summary>
    public static async Task<IReadOnlyList<string>> CheckAsync(
        MvpWorld world, string key, string text, IQuestionTranslator translator, ExpectedAnswer expected)
    {
        var (asked, count) = await world.AskAsync(text, translator);
        if (asked.Outcome != AskOutcome.Answered || count is null)
        {
            var query = asked.Query is null ? "no query" : SearchQueryJson.Serialize(asked.Query);
            return [$"{key} \"{text}\": {asked.Outcome}, not a count ({query})"];
        }

        return [.. expected.Differences(count).Select(d => string.Create(CultureInfo.InvariantCulture, $"{key} \"{text}\": {d}"))];
    }
}
