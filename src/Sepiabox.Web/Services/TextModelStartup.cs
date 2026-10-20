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
/// Loads the CLIP text model before the web takes requests (S-056, D-071), so the first description search after a
/// cold start does not pay for it: verifying and opening the model and embedding one short text. A model that is
/// missing or unusable is logged once as an error and the web starts anyway. The working set after loading is logged
/// too, the measure for the web's 1 GiB.
/// </summary>
public sealed partial class TextModelWarmUp(IQueryEmbedder embedder, ILogger<TextModelWarmUp> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var folder = embedder is ClipQueryEmbedder clip ? clip.ModelFolder : "(not a folder)";
        if (!embedder.IsAvailable)
        {
            // The embedder remembers no missing files (they may still arrive), so this is the one report of it.
            LogMissing(log, folder);
            return;
        }

        var started = TimeProvider.System.GetTimestamp();
        try
        {
            // Off the start-up thread: verifying and opening ~250 MB takes seconds.
            await Task.Run(() => embedder.Embed("a photo"), cancellationToken);
            var milliseconds = TimeProvider.System.GetElapsedTime(started).TotalMilliseconds;
            var workingSet = Environment.WorkingSet / (1024 * 1024);
            LogWarmedUp(log, milliseconds, workingSet);
        }
        catch (VisualSearchUnavailableException)
        {
            // Already logged once as an error by the embedder, with the reason.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "Search by description is off: the CLIP text model or its tokenizer is not in {Folder}.")]
    private static partial void LogMissing(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "CLIP text model ready in {Milliseconds:F0} ms; working set {WorkingSetMegabytes} MB.")]
    private static partial void LogWarmedUp(ILogger logger, double milliseconds, long workingSetMegabytes);
}
