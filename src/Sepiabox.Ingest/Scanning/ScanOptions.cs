using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Ingest.Scanning;

public sealed record ScanOptions(string Root, string? ReportPath, bool FindDuplicates)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out ScanOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        string? root = null;
        string? reportPath = null;
        var findDuplicates = true;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--report":
                    if (i + 1 >= args.Count)
                    {
                        error = "--report needs a file path.";
                        return false;
                    }

                    reportPath = args[++i];
                    break;

                case "--no-duplicates":
                    findDuplicates = false;
                    break;

                case var arg when arg.StartsWith('-'):
                    error = $"Unknown option '{arg}'.";
                    return false;

                case var arg when root is null:
                    root = arg;
                    break;

                default:
                    error = "Only one folder can be scanned at a time.";
                    return false;
            }
        }

        if (root is null)
        {
            error = "Tell scan which folder to read.";
            return false;
        }

        options = new ScanOptions(root, reportPath, findDuplicates);
        error = null;
        return true;
    }
}
