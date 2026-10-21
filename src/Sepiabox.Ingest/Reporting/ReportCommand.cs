using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Reporting;

/// <summary><c>sepiabox-ingest report</c> (S-062): the table, the JSON if asked, and exit code 1 when a manifest was not counted.</summary>
public static class ReportCommand
{
    public static int Run(ReportOptions options, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        var report = AssetReport.Build(options.WorkFolders, new ManifestValidator(), cancellationToken);
        report.WriteTable(output);
        if (options.JsonPath is not null)
        {
            using (var file = File.Create(options.JsonPath))
            {
                report.WriteJson(file);
            }

            output.WriteLine();
            output.WriteLine($"Numbers written to {Path.GetFullPath(options.JsonPath)}");
        }

        return report.Problems.Count == 0 ? 0 : 1;
    }
}
