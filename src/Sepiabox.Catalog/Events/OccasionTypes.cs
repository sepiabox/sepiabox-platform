using System.Text.Json;

namespace Sepiabox.Catalog.Events;

/// <summary>One occasion type CLIP can tell: its key, and the English prompt its text embedding is made from.</summary>
public sealed record OccasionTypeDefinition(string Key, string Prompt);

/// <summary>
/// The occasion types (S-024, D-094): the ones CLIP tells by zero-shot, from <c>eventTypes.json</c>, and <c>trip</c>,
/// which distance and duration decide. Keys are English and stable; the words the family reads come from the web's
/// resources (D-050).
/// </summary>
public static class OccasionTypes
{
    public const string Trip = "trip";

    /// <summary>
    /// The neutral prompt (D-094): in the softmax with the types' prompts, so a photo of a street or a landscape can go to
    /// it rather than to whichever type is least wrong, and never a type itself.
    /// </summary>
    public const string DistractorPrompt = "a photo";

    private static readonly Lazy<IReadOnlyList<OccasionTypeDefinition>> s_clip = new(Load);

    /// <summary>The types told by the photos, in the file's order (which breaks ties).</summary>
    public static IReadOnlyList<OccasionTypeDefinition> Clip => s_clip.Value;

    /// <summary>Every type a DSL query may name: the photos' and <see cref="Trip"/>.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Clip.Select(t => t.Key), Trip];

    public static bool IsKnown(string key) => All.Contains(key, StringComparer.Ordinal);

    private static List<OccasionTypeDefinition> Load()
    {
        using var stream = typeof(OccasionTypes).Assembly.GetManifestResourceStream("eventTypes.json")
            ?? throw new InvalidOperationException("eventTypes.json is not embedded.");
        using var document = JsonDocument.Parse(stream);
        return [.. document.RootElement.GetProperty("types").EnumerateArray()
            .Select(t => new OccasionTypeDefinition(t.GetProperty("key").GetString()!, t.GetProperty("prompt").GetString()!))];
    }
}
