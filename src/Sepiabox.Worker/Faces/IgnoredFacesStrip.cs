using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Storage;

namespace Sepiabox.Worker.Faces;

/// <param name="Assets">Assets that have faces the family deleted (boxes in <c>erased_faces</c>).</param>
/// <param name="Stripped">Manifests written without those faces.</param>
/// <param name="Unchanged">Manifests that had none of them left, or are not in storage.</param>
/// <param name="Problems">Manifests that could not be read or written, by asset id; the next run tries them again.</param>
public sealed record StripResult(int Assets, int Stripped, int Unchanged, IReadOnlyList<string> Problems);

/// <summary>
/// Takes the faces the family deleted out of the stored manifests (#111, D-121): the catalog remembers the boxes of such faces
/// (<c>erased_faces</c>, never an embedding) so a re-import does not bring them back, but the manifest still holds each one with its
/// embedding. A face of the manifest whose box overlaps a remembered box of its asset by at least <see cref="FaceAssigner.SameBoxIoU"/>
/// (the importer's own rule) is removed, only through <see cref="ManifestStore"/> (D-036). It reads the table on every run, not only
/// what changed, so a manifest that an upload from another computer put the faces back in is stripped again the next hour; and it is
/// idempotent: a manifest already stripped is not written. No manifest field is added: a face is simply not listed.
/// </summary>
public sealed class IgnoredFacesStrip(CatalogDbContext db, ManifestStore store, ILogger logger)
{
    public async Task<StripResult> RunAsync(string container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        var erased = (await db.ErasedFaces.AsNoTracking().Select(f => new { f.AssetId, f.Box }).ToListAsync(cancellationToken))
            .GroupBy(f => f.AssetId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        var (stripped, unchanged) = (0, 0);
        var problems = new List<string>();
        foreach (var asset in erased)
        {
            var boxes = asset.Select(f => f.Box).ToList();
            var result = await store.WriteMergedAsync(
                container,
                new FacesStripping(asset.Key, face => boxes.Any(box => FaceMath.IoU(face.Box, box) >= FaceAssigner.SameBoxIoU)),
                cancellationToken);
            switch (result.Status)
            {
                case ManifestWriteStatus.Written:
                    stripped++;
                    break;
                case ManifestWriteStatus.Unchanged:
                    unchanged++;
                    break;
                default:
                    problems.Add(asset.Key);
                    Log.StripProblem(logger, asset.Key, result.Problem ?? "unknown");
                    break;
            }
        }

        Log.IgnoredFacesStripped(logger, erased.Count, stripped, unchanged, problems.Count);
        return new StripResult(erased.Count, stripped, unchanged, problems);
    }
}
