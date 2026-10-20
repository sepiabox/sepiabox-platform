using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sepiabox.Pipeline.Models;

/// <param name="Name">Stable name used in code, such as <c>clip-vit-b32-vision</c>.</param>
/// <param name="File">File name in the models folder.</param>
/// <param name="Url">Where the file is downloaded from, at <paramref name="Revision"/>.</param>
/// <param name="Sha256">Expected SHA-256 of the file, lowercase hex; a file that differs is refused (D-016).</param>
/// <param name="License">The model's license, which must be compatible with the AGPL.</param>
/// <param name="Source">Who published the weights.</param>
/// <param name="Revision">The commit of the model repository the URL points at, so the file cannot change under the pin.</param>
public sealed record ModelInfo(string Name, string File, Uri Url, string Sha256, string License, string Source, string Revision);

/// <summary>
/// The pinned list of ML models (<c>models/models.json</c>, embedded in this assembly). Models are never
/// committed (D-016); <c>sepiabox-ingest models download</c> fetches them and checks their SHA-256.
/// </summary>
public static partial class ModelCatalog
{
    public const string ClipVision = "clip-vit-b32-vision";

    /// <summary>YuNet face detection (S-018).</summary>
    public const string YuNet = "yunet-2023mar";

    /// <summary>SFace face recognition (S-018).</summary>
    public const string SFace = "sface-2021dec";

    private static readonly JsonSerializerOptions s_options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly HashSet<string> s_licenses = new(StringComparer.Ordinal) { "MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause" };

    private static readonly Lazy<IReadOnlyList<ModelInfo>> s_models = new(LoadEmbedded);

    public static IReadOnlyList<ModelInfo> All => s_models.Value;

    public static ModelInfo Get(string name) =>
        All.FirstOrDefault(m => m.Name == name) ?? throw new ArgumentException($"Unknown model '{name}'.", nameof(name));

    /// <summary>Reads a model list; throws <see cref="InvalidDataException"/> for an entry that cannot be trusted.</summary>
    public static IReadOnlyList<ModelInfo> Parse(string json)
    {
        var document = JsonSerializer.Deserialize<ModelList>(json, s_options) ?? throw new InvalidDataException("models.json is empty.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in document.Models ?? [])
        {
            Validate(model);
            if (!names.Add(model.Name))
            {
                throw new InvalidDataException($"models.json lists {model.Name} twice.");
            }
        }

        return document.Models ?? [];
    }

    private static void Validate(ModelInfo model)
    {
        var name = model.Name ?? "(no name)";
        string? problem = null;
        if (model.Name is null || !NamePattern().IsMatch(model.Name))
        {
            problem = "a name of lowercase letters, digits and dashes";
        }
        else if (string.IsNullOrEmpty(model.File) || Path.GetFileName(model.File) != model.File || model.File is "." or "..")
        {
            problem = "a plain file name";
        }
        else if (model.Sha256 is null || !Sha256Pattern().IsMatch(model.Sha256))
        {
            problem = "a SHA-256 of 64 lowercase hex characters";
        }
        else if (model.Revision is null || !RevisionPattern().IsMatch(model.Revision))
        {
            problem = "a revision that is a 40-character commit hash";
        }
        else if (model.Url is null || !model.Url.IsAbsoluteUri || model.Url.Scheme != Uri.UriSchemeHttps
            || !model.Url.AbsolutePath.Contains($"/{model.Revision}/", StringComparison.Ordinal))
        {
            problem = "an absolute https URL that contains its revision";
        }
        else if (model.License is null || !s_licenses.Contains(model.License))
        {
            problem = $"an AGPL-compatible license ({string.Join(", ", s_licenses)})";
        }

        if (problem is not null)
        {
            throw new InvalidDataException($"models.json: {name} needs {problem}.");
        }
    }

    private static IReadOnlyList<ModelInfo> LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("models.json")
            ?? throw new InvalidOperationException("models.json is not embedded in Sepiabox.Pipeline.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex RevisionPattern();

    private sealed record ModelList(IReadOnlyList<ModelInfo>? Models);
}

/// <summary>Where downloaded models live on this computer.</summary>
public static class ModelFolder
{
    /// <summary>Overrides the default folder, for CI and tests.</summary>
    public const string Variable = "SEPIABOX_MODELS";

    /// <summary><c>%LOCALAPPDATA%\Sepiabox\models</c> on Windows, <c>~/.sepiabox/models</c> elsewhere.</summary>
    public static string Default()
    {
        var overridden = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sepiabox", "models")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sepiabox", "models");
    }

    public static string PathOf(string folder, ModelInfo model) => Path.Combine(folder, model.File);
}
