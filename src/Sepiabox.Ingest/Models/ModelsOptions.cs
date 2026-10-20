using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Ingest.Models;

/// <param name="Folder">Where models go; null for the default folder.</param>
/// <param name="Only">The names of the models to download; null for all of them.</param>
public sealed record ModelsOptions(string? Folder, IReadOnlyList<string>? Only = null)
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
        List<string>? only = null;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--only" when i + 1 >= args.Count:
                    error = "--only needs model names separated by commas.";
                    return false;

                case "--only":
                    only = [.. args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                    if (only.Count == 0)
                    {
                        error = "--only needs at least one model name.";
                        return false;
                    }

                    var unknown = only.FirstOrDefault(name => Sepiabox.Search.Models.ModelCatalog.Find(name) is null);
                    if (unknown is not null)
                    {
                        error = $"Unknown model '{unknown}' for --only; the models are "
                            + string.Join(", ", Sepiabox.Search.Models.ModelCatalog.All.Select(m => m.Name)) + ".";
                        return false;
                    }

                    break;

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

        options = new ModelsOptions(folder, only);
        error = null;
        return true;
    }
}
