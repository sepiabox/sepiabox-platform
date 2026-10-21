using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Storage;

namespace Sepiabox.Worker.Faces;

/// <param name="Ran">False when face recognition was never turned off: there is nothing to erase.</param>
/// <param name="Erased">Manifests whose faces the sweep erased (written).</param>
/// <param name="Unchanged">Manifests that already carried the erase.</param>
/// <param name="Problems">Manifests that could not be read or written, by blob name; the next run tries them again.</param>
public sealed record SweepResult(bool Ran, int Erased, int Unchanged, IReadOnlyList<string> Problems);

/// <summary>
/// <c>faces sweep</c> (S-022, D-081): stamps the library's last erase into every manifest blob written since the
/// previous sweep, so faces found before the family turned recognition off leave Blob Storage too, through
/// <see cref="ManifestStore"/> like any other writer (D-036). The first run after an erase goes through every blob;
/// later runs only through blobs written since, which catches uploads of old work folders. The catalog then records how
/// far the blobs are swept, and only a sweep with no problem moves it.
/// </summary>
public sealed class ManifestFacesSweep(CatalogDbContext db, ManifestStore store, TimeProvider clock, ILogger logger)
{
    /// <summary>
    /// How far before its start a sweep records itself: the blobs' times come from storage and this one from the job, and
    /// a blob written during the sweep may be listed before it was written.
    /// </summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(10);

    public async Task<SweepResult> RunAsync(BlobContainerClient manifests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        var state = await FaceRecognition.ReadAsync(db, cancellationToken);
        if (state.ErasedAt is not { } erasedAt)
        {
            return new SweepResult(false, 0, 0, []);
        }

        var start = clock.GetUtcNow();
        var since = state.SweptThrough;
        var (erased, unchanged) = (0, 0);
        var problems = new List<string>();
        try
        {
            await foreach (var blob in manifests.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                if (!blob.Name.EndsWith(".json", StringComparison.Ordinal)
                    || (since is { } swept && blob.Properties.LastModified is { } written && written < swept))
                {
                    continue;
                }

                var result = await store.WriteMergedAsync(manifests, new FacesErasure(blob.Name[..^".json".Length], erasedAt), cancellationToken);
                switch (result.Status)
                {
                    case ManifestWriteStatus.Written:
                        erased++;
                        break;
                    case ManifestWriteStatus.Unchanged:
                        unchanged++;
                        break;
                    default:
                        problems.Add(blob.Name);
                        Log.SweepProblem(logger, blob.Name, result.Problem ?? "unknown");
                        break;
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No manifest was ever uploaded: nothing holds a face.
        }

        if (problems.Count == 0)
        {
            await new FaceRecognition(db, clock).MarkSweptAsync(erasedAt, start - Margin, cancellationToken);
        }

        Log.FacesSwept(logger, erasedAt, erased, unchanged, problems.Count);
        return new SweepResult(true, erased, unchanged, problems);
    }
}
