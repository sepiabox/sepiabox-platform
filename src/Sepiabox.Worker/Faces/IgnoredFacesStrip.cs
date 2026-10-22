using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Storage;

namespace Sepiabox.Worker.Faces;

/// <param name="Assets">Assets visited: those with faces the family deleted (boxes in <c>erased_faces</c>), the new ones or all.</param>
/// <param name="Stripped">Manifests written without those faces.</param>
/// <param name="Unchanged">Manifests that had none of them left, or are not in storage.</param>
/// <param name="Problems">Manifests that could not be read or written, by asset id; the next run tries them again.</param>
public sealed record StripResult(int Assets, int Stripped, int Unchanged, IReadOnlyList<string> Problems)
{
    /// <summary>Whether the run looked at every asset with remembered boxes (the first run and one a day) and not only the new ones.</summary>
    public bool Full { get; init; }

    /// <summary>False when the run stopped at its deadline before every asset was visited: nothing was recorded, and the next run starts again.</summary>
    public bool Finished { get; init; } = true;
}

/// <summary>Bounds of one strip run.</summary>
/// <param name="Concurrency">Manifests stripped at once.</param>
/// <param name="FullPassEvery">How often a run looks at every asset with remembered boxes, not only the new ones.</param>
public sealed record StripOptions(int Concurrency, TimeSpan FullPassEvery)
{
    public static readonly StripOptions Default = new(16, TimeSpan.FromHours(24));
}

/// <summary>
/// Takes the faces the family deleted out of the stored manifests (#111, D-121): the catalog remembers the boxes of such faces
/// (<c>erased_faces</c>, never an embedding) so a re-import does not recreate them, but the manifest still holds each one with its
/// embedding. A face of the manifest whose box overlaps a remembered box of its asset by at least <see cref="FaceAssigner.SameBoxIoU"/>
/// (the importer's own rule) is removed, only through <see cref="ManifestStore"/> (D-036), 16 manifests at a time. No manifest field is
/// added: a face is simply not listed, and a manifest already stripped is not written (idempotent).
/// <para>
/// A run visits only the assets whose boxes were remembered since the last complete run (a checkpoint in <c>settings</c>, no migration),
/// with a margin for the clocks; once a day (<see cref="StripOptions.FullPassEvery"/>) it visits every asset that has any, which heals a
/// manifest that another writer put the faces back in. A run with a problem does not move the checkpoint, so it is tried again.
/// </para>
/// </summary>
public sealed class IgnoredFacesStrip(CatalogDbContext db, ManifestStore store, TimeProvider clock, ILogger logger, StripOptions? options = null)
{
    /// <summary>When the last complete run started: assets with boxes remembered after it (less <see cref="Margin"/>) are the new ones.</summary>
    public const string CheckpointKey = "faces.strip.checkpoint";

    /// <summary>When the last complete full pass started.</summary>
    public const string FullPassKey = "faces.strip.fullpass";

    /// <summary>The slack for the clocks of the web, which writes <c>erased_at</c>, and of the job.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(10);

    private readonly StripOptions _options = options ?? StripOptions.Default;

    /// <param name="container">The container of manifests.</param>
    /// <param name="deadline">When to stop starting new assets (the faces job gives itself 30 minutes and the sweep may have used 25 of them); a run that stops here is not finished and moves no checkpoint. Null: no deadline.</param>
    /// <param name="cancellationToken">Stops the run.</param>
    public async Task<StripResult> RunAsync(string container, DateTimeOffset? deadline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        var started = clock.GetUtcNow();
        var checkpoint = await ReadAsync(CheckpointKey, cancellationToken);
        var lastFull = await ReadAsync(FullPassKey, cancellationToken);
        var full = checkpoint is null || lastFull is null || started - lastFull >= _options.FullPassEvery;
        var since = checkpoint - Margin;

        var rows = db.ErasedFaces.AsNoTracking();
        if (!full)
        {
            rows = rows.Where(f => f.ErasedAt > since);
        }

        var assets = (await rows.Select(f => f.AssetId).Distinct().ToListAsync(cancellationToken)).Order(StringComparer.Ordinal).ToList();
        // The boxes of each visited asset are all of its remembered boxes, not only the new ones.
        var boxes = (await db.ErasedFaces.AsNoTracking().Where(f => assets.Contains(f.AssetId)).Select(f => new { f.AssetId, f.Box }).ToListAsync(cancellationToken))
            .GroupBy(f => f.AssetId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(f => f.Box).ToList(), StringComparer.Ordinal);
        var (stripped, unchanged, notVisited) = (0, 0, 0);
        var problems = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await Parallel.ForEachAsync(
            assets,
            new ParallelOptions { MaxDegreeOfParallelism = _options.Concurrency, CancellationToken = cancellationToken },
            async (asset, stop) =>
            {
                if (deadline is { } limit && clock.GetUtcNow() >= limit)
                {
                    Interlocked.Increment(ref notVisited);
                    return;
                }

                var remembered = boxes[asset];
                var result = await store.WriteMergedAsync(
                    container,
                    new FacesStripping(asset, face => remembered.Any(box => FaceMath.IoU(face.Box, box) >= FaceAssigner.SameBoxIoU)),
                    stop);
                switch (result.Status)
                {
                    case ManifestWriteStatus.Written:
                        Interlocked.Increment(ref stripped);
                        break;
                    case ManifestWriteStatus.Unchanged:
                        Interlocked.Increment(ref unchanged);
                        break;
                    default:
                        problems.Enqueue(asset);
                        Log.StripProblem(logger, asset, result.Problem ?? "unknown");
                        break;
                }
            });

        var finished = notVisited == 0;
        if (problems.IsEmpty && finished)
        {
            // Only a clean, complete run moves the checkpoint: one with a problem, or stopped by its deadline, is tried again from the same place.
            await WriteAsync(CheckpointKey, started, cancellationToken);
            if (full)
            {
                await WriteAsync(FullPassKey, started, cancellationToken);
            }
        }

        Log.IgnoredFacesStripped(logger, assets.Count - notVisited, full, stripped, unchanged, problems.Count);
        if (!finished)
        {
            Log.StripStoppedAtDeadline(logger, notVisited);
        }

        return new StripResult(assets.Count - notVisited, stripped, unchanged, [.. problems.Order(StringComparer.Ordinal)]) { Full = full, Finished = finished };
    }

    private async Task<DateTimeOffset?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var value = await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).SingleOrDefaultAsync(cancellationToken);
        return value is not null && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null;
    }

    private async Task WriteAsync(string key, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var value = at.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var setting = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
