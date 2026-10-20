using Sepiabox.Catalog.Search;
using Sepiabox.Search.Clip;

namespace Sepiabox.Web.Services;

/// <summary>
/// The web's CLIP text encoder (S-016, D-055, D-070): loaded on the first search by description and kept for the
/// life of the process. While the files are missing it is unavailable: the filter bar hides the description box and
/// a search by description is answered with a message, never an error page. A file that is not the pinned one or that
/// ONNX Runtime cannot use keeps it off until a restart; a file that could not be opened is tried again after
/// <see cref="RetryAfter"/>.
/// </summary>
public sealed partial class ClipQueryEmbedder : IQueryEmbedder, IDisposable
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    public delegate ITextEncoder? Loader(out string? warning, out ClipLoadFailure failure);

    private readonly Lock _lock = new();
    private readonly string _modelFolder;
    private readonly Loader _load;
    private readonly Func<bool> _filesPresent;
    private readonly TimeProvider _clock;
    private readonly ILogger<ClipQueryEmbedder> _log;
    private ITextEncoder? _encoder;
    private string? _failure;
    private DateTimeOffset? _retryAt;

    public ClipQueryEmbedder(string modelFolder, ILogger<ClipQueryEmbedder> log, TimeProvider? clock = null)
        : this(
            modelFolder,
            (out string? warning, out ClipLoadFailure failure) => ClipTextEncoder.TryCreate(modelFolder, out warning, out failure),
            () => ClipTextEncoder.FilesPresent(modelFolder),
            log,
            clock)
    {
    }

    /// <summary>With another loader, for tests.</summary>
    internal ClipQueryEmbedder(string modelFolder, Loader load, Func<bool> filesPresent, ILogger<ClipQueryEmbedder> log, TimeProvider? clock = null)
    {
        _modelFolder = modelFolder;
        _load = load;
        _filesPresent = filesPresent;
        _log = log;
        _clock = clock ?? TimeProvider.System;
    }

    public string ModelFolder => _modelFolder;

    public bool IsAvailable
    {
        get
        {
            lock (_lock)
            {
                return _encoder is not null || (CanTry() && _filesPresent());
            }
        }
    }

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

    // Never after a failure that only new files fix; after one that may pass, once the wait is over.
    private bool CanTry() => _failure is null || (_retryAt is { } at && _clock.GetUtcNow() >= at);

    private ITextEncoder Load()
    {
        lock (_lock)
        {
            if (_encoder is not null)
            {
                return _encoder;
            }

            if (CanTry())
            {
                _encoder = _load(out var warning, out var failure);
                if (_encoder is not null)
                {
                    (_failure, _retryAt) = (null, null);
                    LogLoaded(_log, _modelFolder);
                    return _encoder;
                }

                var reason = warning ?? "The CLIP text model could not be loaded.";
                if (failure == ClipLoadFailure.Missing)
                {
                    // Not a failure to remember: the box is hidden while the files are missing, and shown once they arrive.
                    throw new VisualSearchUnavailableException(reason);
                }

                if (reason != _failure)
                {
                    LogUnavailable(_log, reason);
                }

                _failure = reason;
                _retryAt = failure == ClipLoadFailure.Unreadable ? _clock.GetUtcNow() + RetryAfter : null;
            }

            throw new VisualSearchUnavailableException(_failure!);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "CLIP text model loaded from {Folder}.")]
    private static partial void LogLoaded(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search by description is off: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
