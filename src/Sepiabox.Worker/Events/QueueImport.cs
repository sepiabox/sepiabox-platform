using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Events;

/// <summary>
/// <c>import --from-queue</c> (S-033): imports the manifest each event names, one message at a time, until no
/// message arrives within <see cref="IdleWait"/>; the event-driven job then ends and scales back to zero.
/// </summary>
/// <param name="queue">Where the events come from.</param>
/// <param name="importer">Imports each manifest.</param>
/// <param name="manifests">The <c>manifests</c> container the events are about.</param>
/// <param name="logger">Reports ignored messages and the totals.</param>
public sealed class QueueImport(IBlobEventQueue queue, ManifestImporter importer, BlobContainerClient manifests, ILogger<QueueImport> logger)
{
    /// <summary>How long an empty queue is waited on before the job stops.</summary>
    public static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(10);

    public async Task<ImportResult> RunAsync(CancellationToken cancellationToken)
    {
        var problems = new List<ImportProblem>();
        var warnings = new List<ImportProblem>();
        int imported = 0, ignored = 0;

        while (await queue.ReceiveAsync(IdleWait, cancellationToken) is { } message)
        {
            if (message.Event is not { Container: BlobLayout.ManifestsContainer } created)
            {
                // Another container, or not a blob event at all: the subscription's filter should have kept it
                // out, and handling it again would change nothing.
                Log.EventIgnored(logger, message.Id, message.Event?.Container);
                ignored++;
                await queue.CompleteAsync(message, cancellationToken);
                continue;
            }

            ImportResult result;
            try
            {
                result = await importer.ImportOneAsync(manifests, created.BlobName, cancellationToken);
            }
            catch (Exception)
            {
                // The database or storage failed, not the manifest: the message comes back (and is
                // dead-lettered after the queue's last delivery) and the job ends with the failure.
                await AbandonAsync(message);
                throw;
            }

            // A manifest that cannot be imported is reported here and by every import-all run until it is fixed
            // or deleted (D-034); delivering the message again would only fail the same way.
            await queue.CompleteAsync(message, cancellationToken);
            imported += result.Imported;
            problems.AddRange(result.Problems);
            warnings.AddRange(result.Warnings);
        }

        Log.QueueDrained(logger, imported, ignored);
        return new ImportResult(imported, 0, problems, warnings);
    }

    private async Task AbandonAsync(BlobEventMessage message)
    {
        try
        {
            await queue.AbandonAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The lock expires anyway and the message comes back; the original failure is the one to report.
            Log.AbandonFailed(logger, message.Id, ex);
        }
    }
}
