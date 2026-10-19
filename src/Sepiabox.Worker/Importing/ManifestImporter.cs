using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Npgsql;
using Pgvector;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Embeddings;
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

        if (!TryReadEmbeddings(manifest, out var embeddings, out var embeddingProblem))
        {
            return (false, embeddingProblem);
        }

        // Only errors known to be about this manifest's data are reported and the import goes on;
        // everything else (connection, missing table, a schema the model no longer matches,
        // cancellation…) stops the import, rather than failing every manifest left with the same error.
        // Two imports inserting the same new asset at once make one fail with a unique violation
        // (23505): that is reported for its manifest and imported again by the next run.
        // DbUpdateConcurrencyException is deliberately fatal: no concurrency tokens are configured, so
        // it only means an update or delete found its row gone, which is not this manifest's fault.
        try
        {
            await SaveAsync(manifest, kind, embeddings, blobName, etag, cancellationToken);
            return (true, null);
        }
        // Only SaveChanges failures (which EF wraps in DbUpdateException) can be about the data; a read
        // that fails says something about the database. SaveAsync writes only through SaveChangesAsync
        // (no ExecuteUpdate, ExecuteSql or explicit transaction), so no data error escapes unwrapped;
        // a raw write added there needs its own catch.
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && IsDataError(ex.InnerException))
        {
            return (false, $"could not be saved: {Describe(ex.InnerException!)}");
        }
        finally
        {
            // Whatever happened, the next manifest starts from a clean context.
            db.ChangeTracker.Clear();
        }
    }

    private async Task SaveAsync(
        Manifest manifest, string kind, IReadOnlyDictionary<string, float[]> embeddings, string blobName, string etag,
        CancellationToken cancellationToken)
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

        // Like sources, embeddings are only added or replaced per model (D-036): the blob is already a merge.
        foreach (var (model, values) in embeddings)
        {
            var row = await db.AssetEmbeddings.FindAsync([asset.Id, model], cancellationToken);
            if (row is null)
            {
                db.AssetEmbeddings.Add(new AssetEmbedding { AssetId = asset.Id, Model = model, Embedding = new Vector(values) });
            }
            else
            {
                row.Embedding = new Vector(values);
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

    /// <summary>
    /// The embeddings the catalog stores, by model key, with the dimensions of their column. Keys of other
    /// models are left in the manifest for a later story to import.
    /// </summary>
    private static readonly Dictionary<string, int> s_embeddingDimensions = new(StringComparer.Ordinal)
    {
        [ClipImageEmbedder.ModelKey] = ClipImageEmbedder.Dimensions,
    };

    private static bool TryReadEmbeddings(
        Manifest manifest, out IReadOnlyDictionary<string, float[]> embeddings, [NotNullWhen(false)] out string? problem)
    {
        var read = new Dictionary<string, float[]>(StringComparer.Ordinal);
        embeddings = read;
        foreach (var (model, node) in manifest.Embeddings)
        {
            if (!s_embeddingDimensions.TryGetValue(model, out var dimensions))
            {
                continue;
            }

            float[] values;
            try
            {
                values = EmbeddingCodec.Decode(node.GetValue<string>());
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                problem = $"has an unreadable {model} embedding: {ex.Message}";
                return false;
            }

            if (values.Length != dimensions || values.Any(v => !float.IsFinite(v)))
            {
                problem = $"has a {model} embedding of {values.Length} values; expected {dimensions} finite values";
                return false;
            }

            read[model] = values;
        }

        problem = null;
        return true;
    }

    // The schema only allows photo and video; anything else is reported before saving.
    private static string? KindName(MediaKind kind) => kind switch
    {
        MediaKind.Photo => "photo",
        MediaKind.Video => "video",
        _ => null,
    };

    /// <summary>
    /// What SaveChanges can fail with because of the row being written: a data SQLSTATE, or an
    /// InvalidCastException, which Npgsql throws before sending a value that does not fit its column
    /// (a DateTime whose Kind does not match the column type). Outside SaveChanges an
    /// InvalidCastException means reading the database failed, which is not about this manifest.
    /// </summary>
    private static bool IsDataError(Exception? ex) => ex switch
    {
        PostgresException postgres => IsDataSqlState(postgres),
        InvalidCastException => true,
        _ => false,
    };

    /// <summary>
    /// Allow-list of SQLSTATE classes caused by the row: data exception (22), integrity constraint
    /// violation (23), program limit exceeded such as a value too large (54) and PL/pgSQL raise (P0).
    /// </summary>
    private static bool IsDataSqlState(PostgresException ex) => ex.SqlState[..2] is "22" or "23" or "54" or "P0";

    private static string Describe(Exception ex) => ex is PostgresException postgres ? postgres.MessageText : ex.Message;

    // Listings return the ETag bare and downloads return it quoted, as in the HTTP header.
    private static string? Normalize(ETag? etag) => etag?.ToString().Trim('"');
}
