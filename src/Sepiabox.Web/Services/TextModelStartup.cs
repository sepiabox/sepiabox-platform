using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

/// <summary>
/// The CLIP text model in <c>/health</c> (S-056, D-071): Degraded, never Unhealthy, while search by description is
/// unavailable, so the probes keep the web ready and sign-in, the timeline and the other filters keep working. Not
/// tagged <c>live</c>, so <c>/alive</c> ignores it.
/// </summary>
public sealed class TextModelHealthCheck(IQueryEmbedder embedder) : IHealthCheck
{
    public const string Name = "clip-text-model";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(embedder.IsAvailable
            ? HealthCheckResult.Healthy("Search by description is available.")
            : HealthCheckResult.Degraded("Search by description is off: the CLIP text model is missing or cannot be used."));
}

/// <summary>
/// Loads the CLIP text model as the web starts (S-056, D-071), in the background so sign-in and the timeline answer at
/// once after a cold start: verifying and opening the model and embedding one short text. A description search that
/// arrives meanwhile waits for this load rather than starting another. A missing or unusable model, or any other failure
/// to load it, is logged once as an error and the web carries on: this service never stops the host. The working set after loading is logged too, the measure for the web's 1 GiB.
/// </summary>
public sealed partial class TextModelWarmUp(IQueryEmbedder embedder, ILogger<TextModelWarmUp> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var folder = embedder is ClipQueryEmbedder clip ? clip.ModelFolder : "(not a folder)";
        var started = TimeProvider.System.GetTimestamp();
        try
        {
            if (!embedder.IsAvailable)
            {
                // The embedder remembers no missing files (they may still arrive), so this is the one report of it.
                LogMissing(log, folder);
                return;
            }

            // Off the start-up path: verifying and opening ~250 MB takes seconds.
            await Task.Run(() => embedder.Embed("a photo"), stoppingToken);
            var milliseconds = TimeProvider.System.GetElapsedTime(started).TotalMilliseconds;
            var workingSet = Environment.WorkingSet / (1024 * 1024);
            LogWarmedUp(log, milliseconds, workingSet);
        }
        catch (VisualSearchUnavailableException)
        {
            // Already logged once as an error by the embedder, with the reason.
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The web is stopping.
        }
        catch (Exception ex)
        {
            // A failed background service stops the host by default: the web must keep serving everything else.
            LogFailed(log, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Search by description is off: the CLIP text model could not be loaded.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Search by description is off: the CLIP text model or its tokenizer is not in {Folder}.")]
    private static partial void LogMissing(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "CLIP text model ready in {Milliseconds:F0} ms; working set {WorkingSetMegabytes} MB.")]
    private static partial void LogWarmedUp(ILogger logger, double milliseconds, long workingSetMegabytes);
}
