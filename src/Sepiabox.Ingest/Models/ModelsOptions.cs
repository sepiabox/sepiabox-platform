using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Ingest.Models;

/// <param name="Folder">Where models go; null for the default folder.</param>
public sealed record ModelsOptions(string? Folder)
{
    /// <summary>Reads the arguments after <c>models</c>; <c>download</c> is the only subcommand.</summary>
    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out ModelsOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        if (args.Count == 0)
        {
            error = "Tell models what to do: models download.";
            return false;
        }

        if (args[0] != "download")
        {
            error = $"Unknown models command '{args[0]}'. The only one is models download.";
            return false;
        }

        string? folder = null;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--models" when i + 1 >= args.Count:
                    error = "--models needs a value.";
                    return false;

                case "--models":
                    folder = args[++i];
                    break;

                default:
                    error = $"Unknown option '{args[i]}' for models download.";
                    return false;
            }
        }

        options = new ModelsOptions(folder);
        error = null;
        return true;
    }
}
