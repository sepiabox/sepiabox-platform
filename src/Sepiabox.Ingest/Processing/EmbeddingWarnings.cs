namespace Sepiabox.Ingest.Processing;

/// <param name="Path">The file on the drive.</param>
/// <param name="Category">The kind of failure, the same for every file it hits, such as the step and exception type.</param>
/// <param name="Detail">The message for this file, which may name it.</param>
public sealed record EmbeddingProblem(string Path, string Category, string Detail);

/// <summary>
/// Prints the files that have no embedding, grouped by category: a model problem usually hits thousands of files,
/// each with its own message, and a few examples say as much as the full list.
/// </summary>
public static class EmbeddingWarnings
{
    public const int ExamplesPerReason = 5;

    public const int Reasons = 10;

    public static void Write(IReadOnlyList<EmbeddingProblem> problems, TextWriter output)
    {
        if (problems.Count == 0)
        {
            return;
        }

        output.WriteLine($"Warning: {Files(problems.Count)} have no embedding yet; the next run tries again:");
        var groups = problems.GroupBy(p => p.Category, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        foreach (var group in groups.Take(Reasons))
        {
            var files = group.ToList();
            output.WriteLine($"  {group.Key}: {Files(files.Count)}");
            output.WriteLine($"    for example: {files[0].Detail}");
            foreach (var file in files.Take(ExamplesPerReason))
            {
                output.WriteLine($"    {file.Path}");
            }

            if (files.Count > ExamplesPerReason)
            {
                output.WriteLine($"    …and {files.Count - ExamplesPerReason:N0} more");
            }
        }

        if (groups.Count > Reasons)
        {
            var rest = groups.Skip(Reasons).ToList();
            output.WriteLine($"  …and {rest.Count:N0} other reasons ({Files(rest.Sum(g => g.Count()))})");
        }
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count:N0} files";
}
