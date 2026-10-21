using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Ingest.Reporting;

/// <summary><c>report &lt;workdir&gt;... [--json &lt;file&gt;]</c>: the work folders in upload order.</summary>
public sealed record ReportOptions(IReadOnlyList<string> WorkFolders, string? JsonPath)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out ReportOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        var folders = new List<string>();
        string? jsonPath = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--json":
                    if (i + 1 >= args.Count || jsonPath is not null)
                    {
                        error = "--json needs one file path.";
                        return false;
                    }

                    jsonPath = args[++i];
                    break;

                case var arg when arg.StartsWith('-'):
                    error = $"Unknown option '{arg}'.";
                    return false;

                default:
                    folders.Add(args[i]);
                    break;
            }
        }

        if (folders.Count == 0)
        {
            error = "Tell report which work folders to count, in the order they were uploaded.";
            return false;
        }

        options = new ReportOptions(folders, jsonPath);
        error = null;
        return true;
    }
}
