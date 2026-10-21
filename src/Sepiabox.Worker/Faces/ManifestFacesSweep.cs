using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Storage;

namespace Sepiabox.Worker.Faces;

/// <param name="Ran">False when face recognition was never turned off, or another sweep is running: nothing was done.</param>
/// <param name="Finished">Whether the pass reached the last blob; otherwise the next run carries on from its checkpoint.</param>
/// <param name="Erased">Manifests whose faces this run erased (written).</param>
/// <param name="Unchanged">Manifests that already carried the erase.</param>
/// <param name="Problems">Manifests that could not be read or written, by blob name; the next pass tries them again.</param>
public sealed record SweepResult(bool Ran, bool Finished, int Erased, int Unchanged, IReadOnlyList<string> Problems);

/// <summary>Bounds of one <c>faces sweep</c> run.</summary>
/// <param name="Concurrency">Manifests erased at once.</param>
/// <param name="PageSize">Blobs listed per page; the checkpoint is saved after each page.</param>
/// <param name="Budget">How long a run goes on before it saves its checkpoint and stops, inside the job's timeout.</param>
public sealed record SweepOptions(int Concurrency, int PageSize, TimeSpan Budget)
{
    public static readonly SweepOptions Default = new(24, 500, TimeSpan.FromMinutes(25));
}

/// <summary>
/// <c>faces sweep</c> (S-022, D-081): stamps the library's last erase into every manifest blob written since the
/// previous complete pass, so faces found before the family turned recognition off leave Blob Storage too, through
/// <see cref="ManifestStore"/> like any other writer (D-036). The first pass after an erase goes through every blob;
/// later passes only through blobs written since. A pass is resumable: after each page of the listing the catalog keeps
/// the listing's continuation token, so a run stopped by its budget, the job's timeout or a failure carries on where it
/// left off. Only a complete pass with no problem records how far the blobs are swept.
/// </summary>
public sealed class ManifestFacesSweep(CatalogDbContext db, ManifestStore store, TimeProvider clock, ILogger logger, SweepOptions? options = null)
{
    /// <summary>
    /// How far before its start a pass records itself: the blobs' times come from storage and this one from the job, and
    /// a blob written during the pass may be listed before it was written.
    /// </summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(10);

    /// <summary>The session lock that keeps two sweeps (an hourly run overlapping a long one) from running at once.</summary>
    public const long SweepLockKey = 0x5E91AB20;

    private readonly SweepOptions _options = options ?? SweepOptions.Default;

    public async Task<SweepResult> RunAsync(BlobContainerClient manifests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        var state = await FaceRecognition.ReadAsync(db, cancellationToken);
        if (state.ErasedAt is not { } erasedAt)
        {
            return new SweepResult(false, false, 0, 0, []);
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({SweepLockKey}) AS \"Value\"").SingleAsync(cancellationToken))
            {
                Log.SweepBusy(logger);
                return new SweepResult(false, false, 0, 0, []);
            }

            try
            {
                return await SweepAsync(manifests, erasedAt, state.SweptThrough, cancellationToken);
            }
            finally
            {
                await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({SweepLockKey}) AS \"Value\"").SingleAsync(CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<SweepResult> SweepAsync(
        BlobContainerClient manifests, DateTimeOffset erasedAt, DateTimeOffset? sweptThrough, CancellationToken cancellationToken)
    {
        var recognition = new FaceRecognition(db, clock);
        var started = clock.GetUtcNow();
        var pass = await FaceRecognition.ReadSweepAsync(db, cancellationToken)
            ?? new SweepCheckpoint(erasedAt, sweptThrough, started, null, false);
        var (erased, unchanged) = (0, 0);
        var problems = new ConcurrentQueue<string>();
        var stopped = false;
        try
        {
            await foreach (var page in manifests.GetBlobsAsync(cancellationToken: cancellationToken).AsPages(pass.Token, _options.PageSize))
            {
                var names = page.Values
                    .Where(blob => blob.Name.EndsWith(".json", StringComparison.Ordinal)
                        && !(pass.Since is { } since && blob.Properties.LastModified is { } written && written < since))
                    .Select(blob => blob.Name)
                    .ToList();
                var pageProblems = 0;
                await Parallel.ForEachAsync(
                    names,
                    new ParallelOptions { MaxDegreeOfParallelism = _options.Concurrency, CancellationToken = cancellationToken },
                    async (name, token) =>
                    {
                        var result = await store.WriteMergedAsync(manifests, new FacesErasure(name[..^".json".Length], erasedAt), token);
                        switch (result.Status)
                        {
                            case ManifestWriteStatus.Written:
                                Interlocked.Increment(ref erased);
                                break;
                            case ManifestWriteStatus.Unchanged:
                                Interlocked.Increment(ref unchanged);
                                break;
                            default:
                                Interlocked.Increment(ref pageProblems);
                                problems.Enqueue(name);
                                Log.SweepProblem(logger, name, result.Problem ?? "unknown");
                                break;
                        }
                    });

                pass = pass with { Token = page.ContinuationToken, Failed = pass.Failed || pageProblems > 0 };
                if (string.IsNullOrEmpty(page.ContinuationToken))
                {
                    break;
                }

                if (!await recognition.SaveSweepAsync(erasedAt, pass, cancellationToken))
                {
                    // Turned off again meanwhile: that erase has a pass of its own.
                    stopped = true;
                    break;
                }

                if (clock.GetUtcNow() - started >= _options.Budget)
                {
                    stopped = true;
                    break;
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No manifest was ever uploaded: nothing holds a face.
        }

        var finished = !stopped;
        if (finished)
        {
            // A pass with a problem is not complete: the next run starts a new one, which tries those manifests again.
            await recognition.SaveSweepAsync(erasedAt, null, cancellationToken);
            if (!pass.Failed)
            {
                await recognition.MarkSweptAsync(erasedAt, pass.StartedAt - Margin, cancellationToken);
            }
        }

        Log.FacesSwept(logger, erasedAt, finished, erased, unchanged, problems.Count);
        return new SweepResult(true, finished, erased, unchanged, [.. problems.Order(StringComparer.Ordinal)]);
    }
}
