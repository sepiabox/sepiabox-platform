using System.Reflection;
using System.Text.Json;

namespace Sepiabox.Pipeline.Models;

/// <param name="Name">Stable name used in code, such as <c>clip-vit-b32-vision</c>.</param>
/// <param name="File">File name in the models folder.</param>
/// <param name="Url">Where the file is downloaded from.</param>
/// <param name="Sha256">Expected SHA-256 of the file, lowercase hex; a file that differs is refused (D-016).</param>
/// <param name="License">The model's license, which must be compatible with the AGPL.</param>
/// <param name="Source">Who published the weights.</param>
public sealed record ModelInfo(string Name, string File, Uri Url, string Sha256, string License, string Source);

/// <summary>
/// The pinned list of ML models (<c>models/models.json</c>, embedded in this assembly). Models are never
/// committed (D-016); <c>sepiabox-ingest models download</c> fetches them and checks their SHA-256.
/// </summary>
public static class ModelCatalog
{
    public const string ClipVision = "clip-vit-b32-vision";

    private static readonly Lazy<IReadOnlyList<ModelInfo>> s_models = new(LoadEmbedded);

    public static IReadOnlyList<ModelInfo> All => s_models.Value;

    public static ModelInfo Get(string name) =>
        All.FirstOrDefault(m => m.Name == name) ?? throw new ArgumentException($"Unknown model '{name}'.", nameof(name));

    public static IReadOnlyList<ModelInfo> Parse(string json)
    {
        var document = JsonSerializer.Deserialize<ModelList>(json, s_options) ?? throw new JsonException("models.json is empty.");
        return document.Models;
    }

    private static IReadOnlyList<ModelInfo> LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("models.json")
            ?? throw new InvalidOperationException("models.json is not embedded in Sepiabox.Pipeline.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static readonly JsonSerializerOptions s_options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record ModelList(IReadOnlyList<ModelInfo> Models);
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
