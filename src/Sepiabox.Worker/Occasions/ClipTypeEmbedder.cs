using Sepiabox.Catalog.Search;
using Sepiabox.Search.Clip;

namespace Sepiabox.Worker.Occasions;

/// <summary>
/// CLIP's text side for <c>events classify</c> (S-024, D-094): embeds the occasion types' prompts. Loaded on first use
/// from the models folder <c>sepiabox-ingest models download</c> fills (or <c>Search:ModelFolder</c>); a missing or
/// unusable model fails the command with the reason, before any occasion is touched.
/// </summary>
public sealed class ClipTypeEmbedder(string modelFolder) : IQueryEmbedder, IDisposable
{
    private ClipTextEncoder? _encoder;

    public bool IsAvailable => _encoder is not null || ClipTextEncoder.FilesPresent(modelFolder);

    public float[] Embed(string text)
    {
        if (_encoder is null)
        {
            _encoder = ClipTextEncoder.TryCreate(modelFolder, out var warning)
                ?? throw new VisualSearchUnavailableException(warning ?? "The CLIP text model could not be loaded.");
        }

        return _encoder.Encode(text);
    }

    public void Dispose() => _encoder?.Dispose();
}
