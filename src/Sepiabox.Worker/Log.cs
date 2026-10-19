using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Container {Container} does not exist yet; nothing to import.")]
    public static partial void NoManifestContainer(ILogger logger, string container);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping invalid manifest {BlobName}: {Reason}")]
    public static partial void InvalidManifest(ILogger logger, string blobName, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported {Imported} manifests, {Unchanged} unchanged, {Invalid} invalid.")]
    public static partial void ImportFinished(ILogger logger, int imported, int unchanged, int invalid);
}
