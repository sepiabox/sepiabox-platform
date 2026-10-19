using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Container {Container} does not exist yet; nothing to import.")]
    public static partial void NoManifestContainer(ILogger logger, string container);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping manifest {BlobName}: {Reason}")]
    public static partial void ManifestProblem(ILogger logger, string blobName, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported {Imported} manifests, {Unchanged} unchanged, {Invalid} with problems.")]
    public static partial void ImportFinished(ILogger logger, int imported, int unchanged, int invalid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped before finishing; running the same command again continues.")]
    public static partial void Cancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The worker failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The host did not stop cleanly; the exit code reflects the command.")]
    public static partial void StopFailed(ILogger logger, Exception exception);
}
