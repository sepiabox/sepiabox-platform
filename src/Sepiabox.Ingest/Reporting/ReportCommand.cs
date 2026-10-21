using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Reporting;

/// <summary><c>sepiabox-ingest report</c> (S-062): the table, the JSON if asked, and exit code 1 when a manifest was not counted.</summary>
public static class ReportCommand
{
    /// <summary>0 when every manifest was counted; 1 when one was not, or the JSON could not be written; 2 for a JSON path inside a work folder.</summary>
    public static int Run(ReportOptions options, TextWriter output, CancellationToken cancellationToken = default) =>
        Run(options, output, TextWriter.Null, cancellationToken);

    public static int Run(ReportOptions options, TextWriter output, TextWriter errors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);
        if (options.JsonPath is { } json && options.WorkFolders.FirstOrDefault(folder => IsInside(json, folder)) is { } inside)
        {
            // Report only reads the work folders; its own file goes elsewhere.
            errors.WriteLine($"Write the JSON outside the work folders: {Path.GetFullPath(json)} is inside {Path.GetFullPath(inside)}.");
            return 2;
        }

        var report = AssetReport.Build(options.WorkFolders, new ManifestValidator(), cancellationToken);
        report.WriteTable(output);
        if (options.JsonPath is not null)
        {
            var path = Path.GetFullPath(options.JsonPath);
            var temporary = path + ".tmp";
            try
            {
                // Written whole, then moved over the old file: a reader never sees half a report.
                using (var file = File.Create(temporary))
                {
                    report.WriteJson(file);
                }

                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.WriteLine($"Could not write {path}: {ex.Message}");
                return 1;
            }

            output.WriteLine();
            output.WriteLine($"Numbers written to {path}");
        }

        return report.Problems.Count == 0 ? 0 : 1;
    }

    private static bool IsInside(string file, string folder)
    {
        var full = Path.GetFullPath(file);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return full.StartsWith(root, comparison);
    }
}
