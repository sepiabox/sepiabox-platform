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

    // Taken only by the one thread that loads the model; IsAvailable never waits for it.
    private readonly Lock _loading = new();
    private readonly string _modelFolder;
    private readonly Loader _load;
    private readonly Func<bool> _filesPresent;
    private readonly TimeProvider _clock;
    private readonly ILogger<ClipQueryEmbedder> _log;
    private readonly bool _failuresAreErrors;

    // Replaced whole, never changed in place, so a reader sees one consistent state without a lock.
    private volatile State _state = State.NotLoaded;

    /// <param name="modelFolder">Where the text model and tokenizer are.</param>
    /// <param name="log">Where a failure to load is reported.</param>
    /// <param name="clock">For the retry after an I/O failure.</param>
    /// <param name="failuresAreErrors">
    /// Reports a model that cannot be used as an error rather than a warning: in Production, where the image ships it
    /// (S-056, D-071), its absence is a broken deploy.
    /// </param>
    public ClipQueryEmbedder(string modelFolder, ILogger<ClipQueryEmbedder> log, TimeProvider? clock = null, bool failuresAreErrors = false)
        : this(
            modelFolder,
            (out string? warning, out ClipLoadFailure failure) => ClipTextEncoder.TryCreate(modelFolder, out warning, out failure),
            () => ClipTextEncoder.FilesPresent(modelFolder),
            log,
            clock,
            failuresAreErrors)
    {
    }

    /// <summary>With another loader, for tests.</summary>
    internal ClipQueryEmbedder(
        string modelFolder, Loader load, Func<bool> filesPresent, ILogger<ClipQueryEmbedder> log, TimeProvider? clock = null, bool failuresAreErrors = false)
    {
        _modelFolder = modelFolder;
        _load = load;
        _filesPresent = filesPresent;
        _log = log;
        _clock = clock ?? TimeProvider.System;
        _failuresAreErrors = failuresAreErrors;
    }

    public string ModelFolder => _modelFolder;

    /// <summary>
    /// Read on every timeline render: no lock, so it answers at once even while the model loads (it is then true: the
    /// files are there). Checking the files touches the disk, outside any lock.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            var state = _state;
            return state.Encoder is not null || (CanTry(state) && _filesPresent());
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

    public void Dispose() => _state.Encoder?.Dispose();

    // Never after a failure that only new files fix; after one that may pass, once the wait is over.
    private bool CanTry(State state) => state.Failure is null || (state.RetryAt is { } at && _clock.GetUtcNow() >= at);

    private ITextEncoder Load()
    {
        if (_state.Encoder is { } loaded)
        {
            return loaded;
        }

        lock (_loading)
        {
            var state = _state;
            if (state.Encoder is not null)
            {
                return state.Encoder;
            }

            if (CanTry(state))
            {
                ITextEncoder? encoder;
                string? warning;
                ClipLoadFailure failure;
                try
                {
                    encoder = _load(out warning, out failure);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // ONNX Runtime's native library missing, a model that crashes the session, anything TryCreate did
                    // not foresee: kept off until a restart like an invalid file, never thrown at the caller as is.
                    (encoder, warning, failure) = (null, $"The CLIP text model could not be loaded ({ex.GetType().Name}: {ex.Message}).", ClipLoadFailure.Invalid);
                }

                if (encoder is not null)
                {
                    _state = new State(encoder, null, null);
                    LogLoaded(_log, _modelFolder);
                    return encoder;
                }

                var reason = warning ?? "The CLIP text model could not be loaded.";
                if (failure == ClipLoadFailure.Missing)
                {
                    // Not a failure to remember: the box is hidden while the files are missing, and shown once they arrive.
                    throw new VisualSearchUnavailableException(reason);
                }

                if (reason != state.Failure)
                {
                    if (_failuresAreErrors)
                    {
                        LogUnavailableError(_log, reason);
                    }
                    else
                    {
                        LogUnavailable(_log, reason);
                    }
                }

                state = new State(null, reason, failure == ClipLoadFailure.Unreadable ? _clock.GetUtcNow() + RetryAfter : null);
                _state = state;
            }

            throw new VisualSearchUnavailableException(state.Failure!);
        }
    }

    private sealed record State(ITextEncoder? Encoder, string? Failure, DateTimeOffset? RetryAt)
    {
        public static readonly State NotLoaded = new(null, null, null);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "CLIP text model loaded from {Folder}.")]
    private static partial void LogLoaded(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search by description is off: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Search by description is off: {Reason}")]
    private static partial void LogUnavailableError(ILogger logger, string reason);
}
