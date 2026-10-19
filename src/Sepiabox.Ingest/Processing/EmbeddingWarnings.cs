using Sepiabox.Ingest.Scanning;

namespace Sepiabox.Ingest.Processing;

/// <summary>
/// Prints the files that have no embedding, grouped by reason: a model problem usually hits thousands of files
/// with the same message, and a few examples say as much as the full list.
/// </summary>
public static class EmbeddingWarnings
{
    public const int ExamplesPerReason = 5;

    public static void Write(IReadOnlyList<ScanProblem> problems, TextWriter output)
    {
        if (problems.Count == 0)
        {
            return;
        }

        output.WriteLine($"Warning: {problems.Count:N0} files have no embedding yet; the next run tries again:");
        foreach (var group in problems.GroupBy(p => p.Reason, StringComparer.Ordinal).OrderByDescending(g => g.Count()))
        {
            var paths = group.Select(p => p.Path).ToList();
            output.WriteLine($"  {group.Key} ({paths.Count:N0} files)");
            foreach (var path in paths.Take(ExamplesPerReason))
            {
                output.WriteLine($"    {path}");
            }

            if (paths.Count > ExamplesPerReason)
            {
                output.WriteLine($"    …and {paths.Count - ExamplesPerReason:N0} more");
            }
        }
    }
}
