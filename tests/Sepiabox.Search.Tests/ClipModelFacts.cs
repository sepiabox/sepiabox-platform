using Sepiabox.Search.Clip;
using Sepiabox.Search.Models;

namespace Sepiabox.Search.Tests;

/// <summary>A fact that needs CLIP's BPE merges: skipped when they are not downloaded, unless CI requires them.</summary>
internal sealed class ClipBpeFactAttribute : FactAttribute
{
    public ClipBpeFactAttribute()
    {
        if (!File.Exists(ModelFolder.PathOf(TestModels.Folder, ModelCatalog.Get(ModelCatalog.ClipBpe))) && !TestModels.Required)
        {
            Skip = $"The CLIP tokenizer is not in {TestModels.Folder}; run `sepiabox-ingest models download`.";
        }
    }
}

/// <summary>
/// A fact that needs the CLIP text model. Skipped while models.json does not pin it (D-070), even in CI, and
/// otherwise when it is not downloaded unless CI requires it.
/// </summary>
internal sealed class ClipTextFactAttribute : FactAttribute
{
    public ClipTextFactAttribute()
    {
        if (ModelCatalog.Find(ModelCatalog.ClipText) is null)
        {
            Skip = "models.json does not pin the CLIP text model yet (D-070).";
        }
        else if (!ClipTextEncoder.FilesPresent(TestModels.Folder) && !TestModels.Required)
        {
            Skip = $"The CLIP text model is not in {TestModels.Folder}; run `sepiabox-ingest models download`.";
        }
    }
}
