using System.Text.Json.Nodes;

namespace Sepiabox.Acceptance.Tests;

/// <summary>One acceptance question: what it is compared with (<see cref="Expect"/>) and the query that means it.</summary>
public sealed record AcceptanceQuestion(string Id, string Expect, string English, string Portuguese, string Expected)
{
    /// <summary>The question in both languages, keyed as the recorded file keys them: "id/en" and "id/pt".</summary>
    public IEnumerable<(string Key, string Text)> InBothLanguages()
    {
        yield return ($"{Id}/en", English);
        yield return ($"{Id}/pt", Portuguese);
    }
}

/// <summary>
/// S-054's questions (<c>acceptance.questions.json</c>) and the translator's recorded answers
/// (<c>acceptance.recorded.json</c>), read from the source tree so the live run can rewrite the recorded file.
/// </summary>
public static class AcceptanceQuestions
{
    public const string FileName = "acceptance.questions.json";
    public const string RecordedFileName = "acceptance.recorded.json";

    public static string PathOf(string fileName) => Path.Combine(RepositoryRoot(), "tests", "Sepiabox.Acceptance.Tests", "TestData", fileName);

    public static IReadOnlyList<AcceptanceQuestion> Load()
    {
        var root = JsonNode.Parse(File.ReadAllText(PathOf(FileName)))!;
        return [.. root["questions"]!.AsArray().Select(q => new AcceptanceQuestion(
            q!["id"]!.GetValue<string>(),
            q["expect"]!.GetValue<string>(),
            q["en"]!.GetValue<string>(),
            q["pt"]!.GetValue<string>(),
            q["expected"]!.ToJsonString()))];
    }

    /// <summary>The recorded responses, by "id/en" and "id/pt".</summary>
    public static Dictionary<string, string> Recorded()
    {
        var root = JsonNode.Parse(File.ReadAllText(PathOf(RecordedFileName)))!;
        return root["responses"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>());
    }

    /// <summary>The keys whose recorded response is still the expected query rather than the model's answer.</summary>
    public static IReadOnlyList<string> Placeholders()
    {
        var root = JsonNode.Parse(File.ReadAllText(PathOf(RecordedFileName)))!;
        return root["placeholders"] is JsonArray keys ? [.. keys.Select(k => k!.GetValue<string>())] : [];
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sepiabox.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Sepiabox.slnx not found above " + AppContext.BaseDirectory);
    }
}
