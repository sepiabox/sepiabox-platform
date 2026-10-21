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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not settle message {MessageId}; it returns when its lock expires.")]
    public static partial void SettleFailed(ILogger logger, string messageId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Importing {BlobName} failed on delivery {DeliveryCount}; message {MessageId} returns when its lock expires.")]
    public static partial void LeftToExpire(ILogger logger, string messageId, string blobName, int deliveryCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Importing {BlobName} failed on its last delivery ({DeliveryCount}); message {MessageId} was dead-lettered.")]
    public static partial void DeadLettered(ILogger logger, string messageId, string blobName, int deliveryCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not renew the lock of message {MessageId}.")]
    public static partial void RenewFailed(ILogger logger, string messageId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message {MessageId} was imported but its lock had expired; its redelivery imports the manifest again, changing nothing.")]
    public static partial void LockLost(ILogger logger, string messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not erase the faces of manifest {BlobName}: {Reason}")]
    public static partial void SweepProblem(ILogger logger, string blobName, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Swept the face erase of {ErasedAt:O} into the manifests (pass finished: {Finished}): {Erased} erased, {Unchanged} already erased, {Problems} with problems.")]
    public static partial void FacesSwept(ILogger logger, DateTimeOffset erasedAt, bool finished, int erased, int unchanged, int problems);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another faces sweep is running; this one does nothing.")]
    public static partial void SweepBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Cities} GeoNames cities.")]
    public static partial void GeoNamesLoaded(ILogger logger, int cities);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded {File}: SHA-256 {Sha256}, {Bytes} bytes, last modified {LastModified}, from {Uri}.")]
    public static partial void GeoNamesFileDownloaded(ILogger logger, string file, string sha256, long bytes, string lastModified, Uri uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete the temporary folder {Folder}.")]
    public static partial void TemporaryFolderLeft(ILogger logger, string folder, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Named the place of {Assets} assets.")]
    public static partial void PlacesRefreshed(ILogger logger, int assets);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped before finishing; running the same command again continues.")]
    public static partial void Cancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The worker failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The host did not stop cleanly; the exit code reflects the command.")]
    public static partial void StopFailed(ILogger logger, Exception exception);
}
