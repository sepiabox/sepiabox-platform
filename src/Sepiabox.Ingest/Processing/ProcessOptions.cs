using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Ingest.Processing;

public sealed record ProcessOptions(string Root, string OutputPath, string? SourceName)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out ProcessOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        string? root = null;
        string? outputPath = null;
        string? sourceName = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--out" or "--source-name" when i + 1 >= args.Count:
                    error = $"{args[i]} needs a value.";
                    return false;

                case "--out":
                    outputPath = args[++i];
                    break;

                case "--source-name":
                    sourceName = args[++i];
                    break;

                case var arg when arg.StartsWith('-'):
                    error = $"Unknown option '{arg}'.";
                    return false;

                case var arg when root is null:
                    root = arg;
                    break;

                default:
                    error = "Only one folder can be processed at a time.";
                    return false;
            }
        }

        if (root is null)
        {
            error = "Tell process which folder to read.";
            return false;
        }

        if (outputPath is null)
        {
            error = "Tell process where to write its results with --out <folder>.";
            return false;
        }

        options = new ProcessOptions(root, outputPath, string.IsNullOrWhiteSpace(sourceName) ? null : sourceName.Trim());
        error = null;
        return true;
    }
}
