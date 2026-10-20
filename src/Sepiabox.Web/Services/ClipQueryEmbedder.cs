using Sepiabox.Catalog.Search;
using Sepiabox.Search.Clip;

namespace Sepiabox.Web.Services;

/// <summary>
/// The web's CLIP text encoder (S-016, D-055, D-070): loaded on the first search by description and kept for the
/// life of the process. While the files are missing, or after they failed to load, it is unavailable: the filter bar
/// hides the description box and a search by description is answered with a message, never an error page.
/// </summary>
public sealed partial class ClipQueryEmbedder(string modelFolder, ILogger<ClipQueryEmbedder> log) : IQueryEmbedder, IDisposable
{
    private readonly Lock _lock = new();
    private ClipTextEncoder? _encoder;
    private string? _failure;

    public string ModelFolder => modelFolder;

    public bool IsAvailable => _failure is null && (_encoder is not null || ClipTextEncoder.FilesPresent(modelFolder));

    public float[] Embed(string text)
    {
        var encoder = Load();
        try
        {
            return encoder.Encode(text);
        }
        catch (Exception ex) when (ex is Microsoft.ML.OnnxRuntime.OnnxRuntimeException or InvalidOperationException)
        {
            throw new VisualSearchUnavailableException("The CLIP text model could not embed the description.", ex);
        }
    }

    public void Dispose() => _encoder?.Dispose();

    private ClipTextEncoder Load()
    {
        lock (_lock)
        {
            if (_encoder is not null)
            {
                return _encoder;
            }

            if (_failure is null)
            {
                _encoder = ClipTextEncoder.TryCreate(modelFolder, out var warning);
                if (_encoder is not null)
                {
                    LogLoaded(log, modelFolder);
                    return _encoder;
                }

                // Once: the box disappears, so nobody asks again until the web restarts with the files fixed.
                _failure = warning ?? "The CLIP text model could not be loaded.";
                LogUnavailable(log, _failure);
            }

            throw new VisualSearchUnavailableException(_failure);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "CLIP text model loaded from {Folder}.")]
    private static partial void LogLoaded(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search by description is off: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
