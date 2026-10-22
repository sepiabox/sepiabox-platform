using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Container {Container} does not exist yet; nothing to import.")]
    public static partial void NoManifestContainer(ILogger logger, string container);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Processing incoming files without {Step}: {Reason}")]
    public static partial void IncomingStepUnavailable(ILogger logger, string step, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "No connected Google Drive matches the account given to --rescan.")]
    public static partial void DriveRescanNotFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The secret {Name} is not set, or this job has no role on it yet.")]
    public static partial void VaultSecretUnavailable(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Key Vault secret {Name} could not be read (status {Status}).")]
    public static partial void VaultSecretStatus(ILogger logger, string name, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Microsoft Graph no longer honours the delta link of connected account {AccountId}: the OneDrive is listed again from the start.")]
    public static partial void OneDriveResync(ILogger logger, long accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "No Google OAuth client is configured (Auth:Google): there is nothing to sync.")]
    public static partial void DriveSyncNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No OneDrive OAuth client is configured (OneDrive:ClientId, OneDrive:ClientSecret): there is nothing to sync.")]
    public static partial void OneDriveSyncNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The refresh token of connected account {AccountId} cannot be read (another key ring, or damaged); it needs to be connected again.")]
    public static partial void DriveTokenUnreadable(ILogger logger, long accountId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google no longer honours the token of connected account {AccountId}; it needs to be connected again.")]
    public static partial void DriveNeedsReconnecting(ILogger logger, long accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synced connected account {AccountId}: {Staged} file(s) staged in incoming; the first listing is still going on: {Listing}.")]
    public static partial void DriveAccountSynced(ILogger logger, long accountId, int staged, bool listing);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped syncing connected account {AccountId} ({Reason}); the next run goes on from the same page.")]
    public static partial void DriveAccountStopped(ILogger logger, long accountId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Left out file {FileId} of connected account {AccountId}: {Reason}.")]
    public static partial void DriveFileSkipped(ILogger logger, long accountId, string fileId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Drive sync finished: {Accounts} account(s), {Staged} file(s) staged, {Removed} source(s) marked removed, {Skipped} left out, {Reconnect} to connect again, {Failed} stopped early.")]
    public static partial void DriveSyncFinished(ILogger logger, int accounts, int staged, int removed, int skipped, int reconnect, int failed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Incoming run finished: {Processed} processed, {LeftOut} left out (not photos or videos), {Skipped} skipped and kept in incoming (a lifecycle rule deletes them after 30 days).")]
    public static partial void IncomingFinished(ILogger logger, int processed, int leftOut, int skipped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped file in incoming: {Skipped}")]
    public static partial void IncomingSkippedFile(ILogger logger, string skipped);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processed incoming file {BlobName} as {AssetId} ({Mode} mode).")]
    public static partial void IncomingProcessed(ILogger logger, string blobName, string assetId, string mode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Incoming file {BlobName} is already processed: its blob is gone.")]
    public static partial void IncomingAlreadyDone(ILogger logger, string blobName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Left out incoming file {BlobName}: {Reason}")]
    public static partial void IncomingLeftOut(ILogger logger, string blobName, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not process incoming file {BlobName}, which stays in incoming: {Reason}")]
    public static partial void IncomingProblem(ILogger logger, string blobName, string? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Made the video of incoming file {BlobName} without sound: {Reason}")]
    public static partial void IncomingWithoutSound(ILogger logger, string blobName, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Left a step out of asset {AssetId}: {Reason}")]
    public static partial void IncomingStepLeftOut(ILogger logger, string assetId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete the temporary folder {Folder}.")]
    public static partial void IncomingWorkFolderNotDeleted(ILogger logger, string folder, Exception exception);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Swept the face erase of {ErasedAt:O} into the manifests (pass finished: {Finished}, recorded: {Recorded}): {Erased} erased, {Unchanged} already erased, {Problems} with problems.")]
    public static partial void FacesSwept(ILogger logger, DateTimeOffset erasedAt, bool finished, bool recorded, int erased, int unchanged, int problems);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuilt the occasions: {Occasions} occasions with {Assets} assets ({Created} created, {Updated} changed, {Removed} removed).")]
    public static partial void OccasionsRebuilt(ILogger logger, int occasions, int assets, int created, int updated, int removed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Classified {Occasions} occasions without a type: {ByPhotos} by their photos, {Trips} trips, {Untyped} still without one.")]
    public static partial void OccasionsClassified(ILogger logger, int occasions, int byPhotos, int trips, int untyped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped before finishing; running the same command again continues.")]
    public static partial void Cancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The worker failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The host did not stop cleanly; the exit code reflects the command.")]
    public static partial void StopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting asset {AssetId} failed ({ExceptionType}); it is kept, and a rerun finishes it.")]
    public static partial void AssetDeleteFailed(ILogger logger, string assetId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deleting asset {AssetId} hit an unexpected invalid operation: {Reason}")]
    public static partial void AssetDeleteInvalid(ILogger logger, string assetId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "assets delete for source {Source} finished with exit code {ExitCode}.")]
    public static partial void AssetsDeleteFinished(ILogger logger, string source, int exitCode);
}
