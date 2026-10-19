using Azure;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Npgsql;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Storage;

namespace Sepiabox.Worker.Importing;

public sealed record ImportProblem(string BlobName, string Reason);

public sealed record ImportResult(int Imported, int Unchanged, IReadOnlyList<ImportProblem> Problems);

/// <summary>
/// Copies manifests from Blob Storage into the catalog (S-008). Each manifest blob is imported once per
/// version (ETag), so running it again only picks up new or changed manifests. One bad manifest is
/// reported and never stops the others.
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

            var (wasImported, problem) = await ImportBlobAsync(manifests, blob.Name, cancellationToken);
            if (wasImported)
            {
                done++;
            }
            else if (problem is not null)
            {
                // Not recorded as imported: it is reported on every run until someone fixes or deletes it.
                Log.ManifestProblem(logger, blob.Name, problem);
                problems.Add(new ImportProblem(blob.Name, problem));
            }
        }

        return new ImportResult(done, unchanged, problems);
    }

    /// <summary>Imports one manifest blob. Neither imported nor a problem means the blob was gone.</summary>
    private async Task<(bool Imported, string? Problem)> ImportBlobAsync(
        BlobContainerClient manifests, string blobName, CancellationToken cancellationToken)
    {
        string json, etag;
        try
        {
            var download = (await manifests.GetBlobClient(blobName).DownloadContentAsync(cancellationToken)).Value;
            json = download.Content.ToString();
            etag = Normalize(download.Details.ETag)!;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Deleted between the listing and the download: nothing left to import.
            return (false, null);
        }
        catch (RequestFailedException ex)
        {
            return (false, $"could not be downloaded: {ex.Message}");
        }

        if (!validator.TryValidate(json, out var manifest, out var error))
        {
            return (false, error);
        }

        // The blob name is what the next import compares; a manifest stored under another asset's name
        // would make two blobs fight over one asset.
        if (blobName != BlobLayout.ManifestBlobName(manifest.Id))
        {
            return (false, $"describes asset {manifest.Id} but is stored as {blobName}");
        }

        if (KindName(manifest.Kind) is not { } kind)
        {
            return (false, $"has kind {manifest.Kind}, which the catalog does not store");
        }

        // Errors caused by this manifest's data are reported and the import goes on; anything that
        // means the database itself is unavailable stops the import, rather than failing every manifest
        // left with the same error.
        try
        {
            await SaveAsync(manifest, kind, blobName, etag, cancellationToken);
            return (true, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres && IsDataError(postgres))
        {
            return (false, $"could not be saved: {postgres.MessageText}");
        }
        catch (PostgresException ex) when (IsDataError(ex))
        {
            return (false, $"could not be saved: {ex.MessageText}");
        }
        catch (InvalidCastException ex)
        {
            // Npgsql refuses to send a value that does not fit its column before it reaches the server,
            // such as a DateTime whose Kind does not match the column type: a problem with this data.
            return (false, $"could not be saved: {ex.Message}");
        }
        finally
        {
            // Whatever happened, the next manifest starts from a clean context.
            db.ChangeTracker.Clear();
        }
    }

    private async Task SaveAsync(Manifest manifest, string kind, string blobName, string etag, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var asset = await db.Assets
            .Include(a => a.Sources)
            .Include(a => a.Derivatives)
            .SingleOrDefaultAsync(a => a.Id == manifest.Id, cancellationToken);
        if (asset is null)
        {
            asset = new Asset { Id = manifest.Id, Kind = kind, ContentType = manifest.ContentType, ImportedAt = now };
            db.Assets.Add(asset);
        }

        asset.Kind = kind;
        asset.SizeBytes = manifest.SizeBytes;
        asset.ContentType = manifest.ContentType;
        // Wall-clock time from the camera (the schema refuses offsets): stored exactly as written.
        asset.CapturedAt = manifest.CapturedAt is { } captured ? DateTime.SpecifyKind(captured, DateTimeKind.Unspecified) : null;
        asset.Location = manifest.Location is { } at ? s_wgs84.CreatePoint(new Coordinate(at.Longitude, at.Latitude)) : null;
        asset.CameraMake = manifest.Camera?.Make;
        asset.CameraModel = manifest.Camera?.Model;
        asset.Width = manifest.Width;
        asset.Height = manifest.Height;
        asset.DurationSeconds = manifest.DurationSeconds;
        asset.UpdatedAt = now;

        // Sources are only ever added (D-032, D-036): upload already merges every drive into the blob.
        foreach (var source in manifest.Sources)
        {
            var modifiedAt = source.ModifiedAt.ToUniversalTime();
            var existing = asset.Sources.FirstOrDefault(s =>
                source.IsSameFile(new ManifestSource(s.Type, s.Name, s.Path, s.ModifiedAt)));
            if (existing is null)
            {
                asset.Sources.Add(new AssetSource
                {
                    AssetId = asset.Id,
                    Type = source.Type,
                    Name = source.Name,
                    Path = ManifestSource.NormalizePath(source.Path),
                    ModifiedAt = modifiedAt,
                });
            }
            else
            {
                existing.ModifiedAt = modifiedAt;
            }
        }

        // Derivatives describe the files in storage now, so the manifest replaces them.
        asset.Derivatives.RemoveAll(d => manifest.Derivatives.All(m => m.Name != d.Name));
        foreach (var derivative in manifest.Derivatives)
        {
            var blobPath = BlobLayout.DerivativeBlobName(asset.Id, derivative);
            var row = asset.Derivatives.FirstOrDefault(d => d.Name == derivative.Name);
            if (row is null)
            {
                asset.Derivatives.Add(new Derivative
                {
                    AssetId = asset.Id,
                    Name = derivative.Name,
                    BlobPath = blobPath,
                    Width = derivative.Width,
                    Height = derivative.Height,
                    ContentType = derivative.ContentType,
                });
            }
            else
            {
                row.BlobPath = blobPath;
                row.Width = derivative.Width;
                row.Height = derivative.Height;
                row.ContentType = derivative.ContentType;
            }
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
    }

    // The schema only allows photo and video; anything else is reported before saving.
    private static string? KindName(MediaKind kind) => kind switch
    {
        MediaKind.Photo => "photo",
        MediaKind.Video => "video",
        _ => null,
    };

    /// <summary>
    /// SQLSTATE classes that describe the server or the connection, not the row: connection (08),
    /// insufficient resources (53), operator intervention such as a restart (57), system (58), internal (XX).
    /// </summary>
    private static bool IsDataError(PostgresException ex) => ex.SqlState[..2] is not ("08" or "53" or "57" or "58" or "XX");

    // Listings return the ETag bare and downloads return it quoted, as in the HTTP header.
    private static string? Normalize(ETag? etag) => etag?.ToString().Trim('"');
}
