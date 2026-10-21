namespace Sepiabox.Search.Questions;

/// <summary>
/// One list of names the catalog knows, for the translator to map a question onto (S-025): the sources, the cities,
/// the countries, and later people, personal places and occasion types (S-021, S-014, S-024), each with the DSL field
/// it fills.
/// </summary>
/// <param name="Name">What the list is, for the model: "sources".</param>
/// <param name="DslField">The DSL field its values go in: <c>filters.sources</c>.</param>
/// <param name="Values">The names, as the DSL expects them.</param>
public sealed record ContextList(string Name, string DslField, IReadOnlyList<string> Values);

/// <summary>
/// What the translator is told besides the question (D-073): today's date and the library's time zone (for "last
/// summer" and seasons), and the names the catalog knows. Nothing about any photo or person beyond those names.
/// </summary>
/// <param name="Today">Today in <paramref name="TimeZone"/>, as "on this day" takes it (D-069).</param>
/// <param name="TimeZone">The library's time zone id, such as <c>Europe/Lisbon</c>.</param>
/// <param name="Lists">The names the catalog knows, by kind.</param>
public sealed record QuestionContext(DateOnly Today, string TimeZone, IReadOnlyList<ContextList> Lists)
{
    public static QuestionContextBuilder For(DateOnly today, string timeZone) => new(today, timeZone);
}

/// <summary>
/// Builds a <see cref="QuestionContext"/>; each story that adds a filter adds its list here, so the prompt's
/// variable part grows without the prompt changing.
/// </summary>
public sealed class QuestionContextBuilder(DateOnly today, string timeZone)
{
    /// <summary>
    /// The most names of one kind sent: a family's library has far fewer cities or sources, and the cap keeps one
    /// question's tokens bounded whatever the catalog holds.
    /// </summary>
    public const int MaxValuesPerList = 500;

    /// <summary>The longest name sent; longer ones are not something a question names.</summary>
    public const int MaxValueLength = 200;

    private readonly List<ContextList> _lists = [];

    /// <summary>
    /// Adds a list; blank, overlong and repeated names are left out, and the first <see cref="MaxValuesPerList"/> are
    /// kept, so a caller gives the most useful names first. They are sent sorted, the same way for every question.
    /// </summary>
    public QuestionContextBuilder Add(string name, string dslField, IEnumerable<string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(dslField);
        ArgumentNullException.ThrowIfNull(values);
        var kept = values
            .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length <= MaxValueLength && !v.Any(char.IsControl))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxValuesPerList)
            .Order(StringComparer.Ordinal)
            .ToList();
        _lists.Add(new ContextList(name, dslField, kept));
        return this;
    }

    public QuestionContext Build() => new(today, timeZone, [.. _lists]);
}
