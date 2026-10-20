using Microsoft.Extensions.Logging;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Events;

/// <summary>Imports the manifest with this blob name (<see cref="ManifestImporter.ImportOneAsync"/>).</summary>
public delegate Task<ImportResult> ImportManifest(string blobName, CancellationToken cancellationToken);

/// <param name="MaxDeliveryCount">The queue's maximum delivery count: the last delivery dead-letters a failure.</param>
/// <param name="LockRenewal">How often the lock of the message being imported is renewed; well under the lock duration.</param>
/// <param name="IdleWait">How long an empty queue is waited on before the job stops.</param>
public sealed record QueueImportOptions(int MaxDeliveryCount, TimeSpan LockRenewal, TimeSpan IdleWait)
{
    /// <summary>The queue's 5 deliveries and 5-minute lock (D-063).</summary>
    public static QueueImportOptions Default { get; } = new(5, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10));
}

/// <summary>
/// <c>import --from-queue</c> (S-033, D-063): imports the manifest each event names, one message at a time,
/// until no message arrives within <see cref="QueueImportOptions.IdleWait"/>; the event-driven job then ends
/// and scales back to zero.
/// </summary>
/// <param name="queue">Where the events come from.</param>
/// <param name="importManifest">Imports one manifest of the <c>manifests</c> container.</param>
/// <param name="options">Delivery count, lock renewal and idle wait.</param>
/// <param name="logger">Reports ignored messages, failures and the totals.</param>
public sealed class QueueImport(IBlobEventQueue queue, ImportManifest importManifest, QueueImportOptions options, ILogger<QueueImport> logger)
{
    public async Task<ImportResult> RunAsync(CancellationToken cancellationToken)
    {
        var problems = new List<ImportProblem>();
        var warnings = new List<ImportProblem>();
        int imported = 0, ignored = 0;

        while (await queue.ReceiveAsync(options.IdleWait, cancellationToken) is { } message)
        {
            if (message.Event is not { Container: BlobLayout.ManifestsContainer } created)
            {
                // Another container, or not a blob event at all: the subscription's filter should have kept it
                // out, and handling it again would change nothing.
                Log.EventIgnored(logger, message.Id, message.Event?.Container);
                ignored++;
                await CompleteAsync(message, cancellationToken);
                continue;
            }

            ImportResult result;
            try
            {
                result = await ImportWithLockAsync(message, created.BlobName, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stopped (the job's timeout, or a new revision): nothing failed, so the message comes back at once.
                await SettleQuietlyAsync(message, m => queue.AbandonAsync(m, CancellationToken.None));
                throw;
            }
            catch (Exception ex)
            {
                // The database or storage failed, not the manifest. The message is left locked, so it comes back
                // only when its lock expires: the queue's deliveries are spread over about 25 minutes, which
                // rides out a restart of the database, instead of being used up in seconds. The last delivery
                // dead-letters it with the reason.
                if (message.DeliveryCount >= options.MaxDeliveryCount)
                {
                    var reason = $"Import failed on delivery {message.DeliveryCount}: {ex.GetType().Name}";
                    await SettleQuietlyAsync(message, m => queue.DeadLetterAsync(m, reason, CancellationToken.None));
                    Log.DeadLettered(logger, message.Id, created.BlobName, message.DeliveryCount);
                }
                else
                {
                    Log.LeftToExpire(logger, message.Id, created.BlobName, message.DeliveryCount);
                }

                throw;
            }

            // A manifest that cannot be imported is reported here and by every import-all run until it is fixed
            // or deleted (D-034); delivering the message again would only fail the same way.
            await CompleteAsync(message, cancellationToken);
            imported += result.Imported;
            problems.AddRange(result.Problems);
            warnings.AddRange(result.Warnings);
        }

        Log.QueueDrained(logger, imported, ignored);
        return new ImportResult(imported, 0, problems, warnings);
    }

    /// <summary>Imports while renewing the message's lock, so a long import never loses it.</summary>
    private async Task<ImportResult> ImportWithLockAsync(BlobEventMessage message, string blobName, CancellationToken cancellationToken)
    {
        using var importing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewing = RenewLockAsync(message, importing.Token);
        try
        {
            return await importManifest(blobName, cancellationToken);
        }
        finally
        {
            await importing.CancelAsync();
            await renewing;
        }
    }

    private async Task RenewLockAsync(BlobEventMessage message, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(options.LockRenewal);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    await queue.RenewLockAsync(message, stop);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The import goes on; if the lock is really lost, completing reports it and the message is
                    // imported again, which changes nothing.
                    Log.RenewFailed(logger, message.Id, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The import finished.
        }
    }

    private async Task CompleteAsync(BlobEventMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await queue.CompleteAsync(message, cancellationToken);
        }
        catch (BlobEventLockLostException)
        {
            // The import is done; the redelivered message imports the same manifest again, changing nothing.
            Log.LockLost(logger, message.Id);
        }
    }

    private async Task SettleQuietlyAsync(BlobEventMessage message, Func<BlobEventMessage, Task> settle)
    {
        try
        {
            await settle(message);
        }
        catch (Exception ex)
        {
            // The lock expires anyway and the message comes back; the original failure is the one to report.
            Log.SettleFailed(logger, message.Id, ex);
        }
    }
}
