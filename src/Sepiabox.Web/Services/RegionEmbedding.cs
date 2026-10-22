using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Clip;

namespace Sepiabox.Web.Services;

/// <summary>Reads the bytes of the preview a region is cut from (S-041, D-099).</summary>
public interface IPreviewSource
{
    /// <summary>
    /// The encoded preview of the photo, or null when the catalog has no such asset or no preview of it. Throws
    /// <see cref="InvalidOperationException"/> when the preview cannot be fetched.
    /// </summary>
    Task<byte[]?> ReadAsync(string assetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches a preview through the same signed link the browser gets (D-039), so the web needs no storage client of its
/// own for it; the preview is the 1280 pixel one, or the 2048 pixel one of an index-only photo (S-063), or a video's poster.
/// </summary>
public sealed class SignedLinkPreviewSource(IDbContextFactory<CatalogDbContext> contexts, ISasUrlFactory links, IHttpClientFactory http) : IPreviewSource
{
    public const string HttpClientName = "previews";

    /// <summary>A preview is a few hundred kilobytes; more than this is not one.</summary>
    public const long MaxBytes = 32 * 1024 * 1024;

    private static readonly string[] s_order = [DerivativeNames.Preview, DerivativeNames.LargePreview, DerivativeNames.Poster];

    public async Task<byte[]?> ReadAsync(string assetId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = await db.Derivatives.AsNoTracking()
            .Where(d => d.AssetId == assetId && s_order.Contains(d.Name))
            .Select(d => new { d.Name, d.BlobPath })
            .ToListAsync(cancellationToken);
        var path = s_order.Select(name => found.FirstOrDefault(d => d.Name == name)?.BlobPath).FirstOrDefault(p => p is not null);
        if (path is null)
        {
            return null;
        }

        var link = await links.ForDerivativeAsync(path, cancellationToken);
        try
        {
            using var client = http.CreateClient(HttpClientName);
            using var response = await client.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                throw new InvalidOperationException("The preview is larger than a preview can be.");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("The preview could not be fetched from storage.", ex);
        }
    }
}

/// <summary>
/// "Photos like this part of a photo" (S-041, D-099): cuts the region from the photo's preview and embeds it with CLIP's
/// image model, the one <c>process</c> used for the stored embeddings. The model is loaded on the first use and kept for
/// the life of the process, like the text model (D-070); while its file is missing the region selection is hidden, and a
/// search for one is answered with a message, never an error page.
/// </summary>
public sealed partial class ClipRegionEmbedder : IRegionEmbedder, IDisposable
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    public delegate ClipImageEncoder? Loader(out string? warning, out ClipLoadFailure failure);

    private readonly Lock _loading = new();
    private readonly string _modelFolder;
    private readonly Loader _load;
    private readonly Func<bool> _filesPresent;
    private readonly IPreviewSource _previews;
    private readonly TimeProvider _clock;
    private readonly ILogger<ClipRegionEmbedder> _log;

    private ClipImageEncoder? _encoder;
    private DateTimeOffset? _failedUntil;
    private bool _failedForGood;

    public ClipRegionEmbedder(string modelFolder, IPreviewSource previews, ILogger<ClipRegionEmbedder> log, TimeProvider? clock = null)
        : this(
            modelFolder,
            (out string? warning, out ClipLoadFailure failure) => ClipImageEncoder.TryCreate(modelFolder, out warning, out failure),
            () => ClipImageEncoder.FilesPresent(modelFolder),
            previews,
            log,
            clock)
    {
    }

    /// <summary>With another loader, for tests.</summary>
    internal ClipRegionEmbedder(
        string modelFolder, Loader load, Func<bool> filesPresent, IPreviewSource previews, ILogger<ClipRegionEmbedder> log, TimeProvider? clock = null)
    {
        _modelFolder = modelFolder;
        _load = load;
        _filesPresent = filesPresent;
        _previews = previews;
        _log = log;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>True while the model is loaded or its file is there to load; false after a file that cannot be used.</summary>
    public bool IsAvailable => _encoder is not null || (!_failedForGood && !WaitingToRetry && _filesPresent());

    private bool WaitingToRetry => _failedUntil is { } until && _clock.GetUtcNow() < until;

    public async Task<float[]> EmbedAsync(string assetId, IReadOnlyList<double> box, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (!RegionCrop.IsValid(box))
        {
            throw new ArgumentException("The box must be [x, y, width, height], relative to the photo.", nameof(box));
        }

        var encoder = Load();
        var photo = await _previews.ReadAsync(assetId, cancellationToken)
            ?? throw new AssetNotAnalysedException(assetId, null);
        try
        {
            // Cutting and embedding take tens of milliseconds, off the request's thread.
            return await Task.Run(() => encoder.Encode(RegionCrop.Prepare(photo, box)), cancellationToken);
        }
        catch (Exception ex) when (ex is Microsoft.ML.OnnxRuntime.OnnxRuntimeException or InvalidOperationException or ImageMagick.MagickException)
        {
            throw new VisualSearchUnavailableException("The CLIP image model could not embed the region.", ex);
        }
    }

    public void Dispose() => _encoder?.Dispose();

    private ClipImageEncoder Load()
    {
        if (_encoder is { } loaded)
        {
            return loaded;
        }

        lock (_loading)
        {
            if (_encoder is { } already)
            {
                return already;
            }

            if (_failedForGood || WaitingToRetry)
            {
                throw new VisualSearchUnavailableException("The CLIP image model cannot be used.");
            }

            ClipImageEncoder? encoder;
            string? warning;
            ClipLoadFailure failure;
            try
            {
                encoder = _load(out warning, out failure);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (encoder, warning, failure) = (null, $"The CLIP image model could not be loaded ({ex.GetType().Name}: {ex.Message}).", ClipLoadFailure.Invalid);
            }

            if (encoder is not null)
            {
                _encoder = encoder;
                LogLoaded(_log, _modelFolder);
                return encoder;
            }

            var reason = warning ?? "The CLIP image model could not be loaded.";
            if (failure != ClipLoadFailure.Missing)
            {
                LogUnavailable(_log, reason);
                if (failure == ClipLoadFailure.Unreadable)
                {
                    _failedUntil = _clock.GetUtcNow() + RetryAfter;
                }
                else
                {
                    _failedForGood = true;
                }
            }

            throw new VisualSearchUnavailableException(reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "CLIP image model loaded from {Folder}.")]
    private static partial void LogLoaded(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search by a region of a photo is off: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
