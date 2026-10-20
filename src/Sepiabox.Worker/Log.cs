using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Container {Container} does not exist yet; nothing to import.")]
    public static partial void NoManifestContainer(ILogger logger, string container);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping manifest {BlobName}: {Reason}")]
    public static partial void ManifestProblem(ILogger logger, string blobName, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Imported manifest {BlobName} without an embedding: {Reason}")]
    public static partial void EmbeddingLeftOut(ILogger logger, string blobName, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Application role {Role} has its login and the catalog's rows.")]
    public static partial void AppRoleEnsured(ILogger logger, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported {Imported} manifests, {Unchanged} unchanged, {Invalid} with problems, {Warnings} without an embedding.")]
    public static partial void ImportFinished(ILogger logger, int imported, int unchanged, int invalid, int warnings);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored message {MessageId}: not a manifest created in the manifests container (container: {Container}).")]
    public static partial void EventIgnored(ILogger logger, string messageId, string? container);

    [LoggerMessage(Level = LogLevel.Information, Message = "The queue is empty: imported {Imported} manifests, ignored {Ignored} messages.")]
    public static partial void QueueDrained(ILogger logger, int imported, int ignored);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not give message {MessageId} back to the queue; it returns when its lock expires.")]
    public static partial void AbandonFailed(ILogger logger, string messageId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped before finishing; running the same command again continues.")]
    public static partial void Cancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The worker failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The host did not stop cleanly; the exit code reflects the command.")]
    public static partial void StopFailed(ILogger logger, Exception exception);
}
