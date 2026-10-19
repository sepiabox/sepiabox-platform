using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;

namespace Sepiabox.Worker.Importing;

public sealed record ImportProblem(string BlobName, string Reason);

public sealed record ImportResult(int Imported, int Unchanged, IReadOnlyList<ImportProblem> Problems);

/// <summary>
/// Copies manifests from Blob Storage into the catalog (S-008). Each manifest blob is imported once per
/// version (ETag), so running it again only picks up new or changed manifests.
/// </summary>
public sealed class ManifestImporter(
    CatalogDbContext db, ManifestValidator validator, TimeProvider clock, ILogger<ManifestImporter> logger)
{
    private static readonly GeometryFactory s_wgs84 = new(new PrecisionModel(), 4326);

    public async Task<ImportResult> ImportOnceAsync(BlobContainerClient manifests, CancellationToken cancellationToken)
    {
        if (!await manifests.ExistsAsync(cancellationToken))
        {
            Log.NoManifestContainer(logger, manifests.Name);
            return new ImportResult(0, 0, []);
        }

        var imported = await db.ManifestImports.AsNoTracking()
            .ToDictionaryAsync(i => i.BlobName, i => i.ETag, StringComparer.Ordinal, cancellationToken);
        var problems = new List<ImportProblem>();
        int done = 0, unchanged = 0;

        await foreach (var blob in manifests.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            var etag = Normalize(blob.Properties.ETag);
            if (etag is not null && imported.TryGetValue(blob.Name, out var known) && known == etag)
            {
                unchanged++;
                continue;
            }

            var content = await manifests.GetBlobClient(blob.Name).DownloadContentAsync(cancellationToken);
            var (manifest, error) = validator.Validate(content.Value.Content.ToString());
            if (manifest is null)
            {
                // Not recorded as imported: it is reported on every run until someone fixes or deletes it.
                Log.InvalidManifest(logger, blob.Name, error!);
                problems.Add(new ImportProblem(blob.Name, error!));
                continue;
            }

            await SaveAsync(manifest, blob.Name, Normalize(content.Value.Details.ETag)!, cancellationToken);
            done++;
        }

        return new ImportResult(done, unchanged, problems);
    }

    // Listings return the ETag bare and downloads return it quoted, as in the HTTP header.
    private static string? Normalize(Azure.ETag? etag) => etag?.ToString().Trim('"');

    private async Task SaveAsync(Manifest manifest, string blobName, string etag, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var asset = await db.Assets
            .Include(a => a.Sources)
            .Include(a => a.Derivatives)
            .SingleOrDefaultAsync(a => a.Id == manifest.Id, cancellationToken);
        if (asset is null)
        {
            asset = new Asset { Id = manifest.Id, Kind = string.Empty, ContentType = string.Empty };
            db.Assets.Add(asset);
        }

        asset.Kind = manifest.Kind.ToString().ToLowerInvariant();
        asset.SizeBytes = manifest.SizeBytes;
        asset.ContentType = manifest.ContentType;
        asset.CapturedAt = manifest.CapturedAt;
        asset.Location = manifest.Location is { } at ? s_wgs84.CreatePoint(new Coordinate(at.Longitude, at.Latitude)) : null;
        asset.CameraMake = manifest.Camera?.Make;
        asset.CameraModel = manifest.Camera?.Model;
        asset.Width = manifest.Width;
        asset.Height = manifest.Height;
        asset.DurationSeconds = manifest.DurationSeconds;
        asset.ImportedAt = now;

        // Sources are only ever added (D-032): each work folder's manifest lists only the copies it saw,
        // and a second drive's manifest overwrites the same blob.
        foreach (var source in manifest.Sources)
        {
            var existing = asset.Sources.FirstOrDefault(s =>
                s.Type == source.Type && s.Name == source.Name && string.Equals(s.Path, source.Path, StringComparison.Ordinal));
            if (existing is null)
            {
                asset.Sources.Add(new AssetSource
                {
                    AssetId = asset.Id,
                    Type = source.Type,
                    Name = source.Name,
                    Path = source.Path,
                    ModifiedAt = source.ModifiedAt.ToUniversalTime(),
                });
            }
            else
            {
                existing.ModifiedAt = source.ModifiedAt.ToUniversalTime();
            }
        }

        // Derivatives describe the current files, so the manifest replaces them.
        asset.Derivatives.RemoveAll(d => manifest.Derivatives.All(m => m.Name != d.Name));
        foreach (var derivative in manifest.Derivatives)
        {
            var row = asset.Derivatives.FirstOrDefault(d => d.Name == derivative.Name);
            if (row is null)
            {
                row = new Derivative { AssetId = asset.Id, Name = derivative.Name, BlobPath = string.Empty, ContentType = string.Empty };
                asset.Derivatives.Add(row);
            }

            row.BlobPath = BlobLayout.DerivativeBlobName(asset.Id, derivative);
            row.Width = derivative.Width;
            row.Height = derivative.Height;
            row.ContentType = derivative.ContentType;
        }

        var import = await db.ManifestImports.FindAsync([blobName], cancellationToken);
        if (import is null)
        {
            db.ManifestImports.Add(new ManifestImport { BlobName = blobName, ETag = etag, ImportedAt = now });
        }
        else
        {
            import.ETag = etag;
            import.ImportedAt = now;
        }

        // One SaveChanges per manifest: the asset and its import record commit together or not at all.
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }
}
