using Sepiabox.Pipeline.Models;

namespace Sepiabox.Tests.Shared;

/// <summary>
/// Where tests find downloaded ML models. CI downloads them and sets <c>SEPIABOX_REQUIRE_MODELS=1</c>, so a
/// missing model fails there; on a machine without them, model tests are skipped with a reason.
/// </summary>
internal static class TestModels
{
    public static string Folder => ModelFolder.Default();

    /// <summary>
    /// A folder that never holds models, for tests that must behave the same whether or not a developer has
    /// downloaded them: no embeddings are computed.
    /// </summary>
    public static string None { get; } = Path.Combine(Path.GetTempPath(), "sepiabox-tests-no-models");

    public static bool Required => Environment.GetEnvironmentVariable("SEPIABOX_REQUIRE_MODELS") == "1";

    public static bool ClipAvailable =>
        File.Exists(ModelFolder.PathOf(Folder, ModelCatalog.Get(ModelCatalog.ClipVision)));
}

/// <summary>A fact that needs the CLIP model: skipped when it is not downloaded, unless CI requires it.</summary>
internal sealed class ClipFactAttribute : FactAttribute
{
    public ClipFactAttribute()
    {
        if (!TestModels.ClipAvailable && !TestModels.Required)
        {
            Skip = $"The CLIP model is not in {TestModels.Folder}; run `sepiabox-ingest models download`.";
        }
    }
}

/// <summary>A theory that needs the CLIP model: skipped when it is not downloaded, unless CI requires it.</summary>
internal sealed class ClipTheoryAttribute : TheoryAttribute
{
    public ClipTheoryAttribute()
    {
        if (!TestModels.ClipAvailable && !TestModels.Required)
        {
            Skip = $"The CLIP model is not in {TestModels.Folder}; run `sepiabox-ingest models download`.";
        }
    }
}
