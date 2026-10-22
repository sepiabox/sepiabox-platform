using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Npgsql;
using Pgvector;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Storage;

namespace Sepiabox.Worker.Importing;

public sealed record ImportProblem(string BlobName, string Reason);

/// <summary>
/// What an import did. <see cref="Problems"/> are manifests that were not imported and make the exit code
/// non-zero; <see cref="Warnings"/> are manifests imported without an embedding or a face that could not be stored
/// (D-043).
/// </summary>
public sealed record ImportResult(int Imported, int Unchanged, IReadOnlyList<ImportProblem> Problems, IReadOnlyList<ImportProblem> Warnings);

/// <summary>
/// Copies manifests from Blob Storage into the catalog (S-008). Each manifest blob is imported once per
/// version (ETag), so running it again only picks up new or changed manifests. One bad manifest is
/// reported and never stops the others. Faces go into people as they arrive (S-019, <see cref="FaceAssigner"/>).
/// </summary>
public sealed class ManifestImporter(
    CatalogDbContext db,
    IBlobStore store,
    ManifestValidator validator,
    IPlaceResolver places,
    TimeProvider clock,
    ILogger<ManifestImporter> logger,
    FaceAssignerOptions? faceOptions = null)
{
    private readonly FaceAssigner _faces = new(db, faceOptions);

    private static readonly GeometryFactory s_wgs84 = new(new PrecisionModel(), 4326);

    /// <param name="container">The container of manifests, <see cref="BlobLayout.ManifestsContainer"/>.</param>
    /// <param name="cancellationToken">Stops the import.</param>
    public async Task<ImportResult> ImportOnceAsync(string container, CancellationToken cancellationToken)
    {
        if (!await store.ContainerExistsAsync(container, cancellationToken))
        {
            Log.NoManifestContainer(logger, container);
            return new ImportResult(0, 0, [], []);
        }

        var imported = await db.ManifestImports.AsNoTracking()
            .ToDictionaryAsync(i => i.BlobName, i => i.ETag, StringComparer.Ordinal, cancellationToken);
        var problems = new List<ImportProblem>();
        var warnings = new List<ImportProblem>();
        int done = 0, unchanged = 0;

        await foreach (var blob in store.ListAsync(container, cancellationToken: cancellationToken))
        {
            var etag = blob.Version;
            if (imported.TryGetValue(blob.Name, out var known) && known == etag)
            {
                unchanged++;
                continue;
            }

            var (wasImported, problem) = await ImportBlobAsync(container, blob.Name, transientFailuresThrow: false, cancellationToken);
            if (Record(blob.Name, wasImported, problem, problems, warnings))
            {
                done++;
            }
        }

        return new ImportResult(done, unchanged, problems, warnings);
    }

    /// <summary>
    /// Imports the one manifest an event named (S-033). A manifest imported before is imported again, which
    /// changes nothing (D-032); a manifest gone by now is neither imported nor a problem. A failure to download
    /// it, and a unique violation from another import saving the same new asset at the same time, are thrown,
    /// not reported, so the event is delivered again instead of being dropped.
    /// </summary>
    public async Task<ImportResult> ImportOneAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        var problems = new List<ImportProblem>();
        var warnings = new List<ImportProblem>();
        var (wasImported, problem) = await ImportBlobAsync(container, blobName, transientFailuresThrow: true, cancellationToken);
        var done = Record(blobName, wasImported, problem, problems, warnings) ? 1 : 0;
        return new ImportResult(done, 0, problems, warnings);
    }

    /// <summary>Logs and lists what happened to one manifest; true when it was imported.</summary>
    private bool Record(string blobName, bool imported, string? problem, List<ImportProblem> problems, List<ImportProblem> warnings)
    {
        if (imported)
        {
            // Imported without an unusable embedding: a warning, reported once, when it is imported (D-043).
            if (problem is not null)
            {
                Log.EmbeddingLeftOut(logger, blobName, problem);
                warnings.Add(new ImportProblem(blobName, problem));
            }

            return true;
        }

        if (problem is not null)
        {
            // Not recorded as imported: it is reported on every run until someone fixes or deletes it.
            Log.ManifestProblem(logger, blobName, problem);
            problems.Add(new ImportProblem(blobName, problem));
        }

        return false;
    }

    /// <summary>
    /// Imports one manifest blob. Neither imported nor a problem means the blob was gone; imported with a problem
    /// means an embedding was left out.
    /// </summary>
    private async Task<(bool Imported, string? Problem)> ImportBlobAsync(
        string container, string blobName, bool transientFailuresThrow, CancellationToken cancellationToken)
    {
        string json, etag;
        DateTimeOffset writtenAt;
        try
        {
            var download = await store.ReadAsync(container, blobName, cancellationToken);
            if (download is null)
            {
                // Deleted between the listing and the download: nothing left to import.
                return (false, null);
            }

            json = download.Content.ToString();
            etag = download.Version;
            writtenAt = download.LastModified;
        }
        catch (Exception ex) when (!transientFailuresThrow && store.IsStoreFailure(ex))
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

        var (embeddings, embeddingProblem) = ReadEmbeddings(manifest);
        var (faces, faceProblem) = ReadFaces(manifest);
        if (faces is not null && !ManifestMerge.DetectionTrusted(manifest, writtenAt))
        {
            // Found "after" the blob was written: the computer's clock was ahead when it ran process, so the time cannot
            // be compared with an erase (D-081). Like a manifest that did not look for faces.
            faces = null;
            const string Ahead = "says its faces were found after it was uploaded (a clock ahead), so they were left out";
            faceProblem = faceProblem is null ? Ahead : $"{faceProblem}; {Ahead}";
        }
        var leftOut = embeddingProblem is null ? faceProblem : faceProblem is null ? embeddingProblem : $"{embeddingProblem}; {faceProblem}";

        // Only errors known to be about this manifest's data are reported and the import goes on;
        // everything else (connection, missing table, a schema the model no longer matches,
        // cancellation…) stops the import, rather than failing every manifest left with the same error.
        // Two imports inserting the same new asset at once make one fail with a unique violation
        // (23505): that is reported for its manifest and imported again by the next run.
        // DbUpdateConcurrencyException is deliberately fatal: no concurrency tokens are configured, so
        // it only means an update or delete found its row gone, which is not this manifest's fault.
        try
        {
            await SaveAsync(manifest, kind, embeddings, faces, blobName, etag, cancellationToken);
            return (true, leftOut);
        }
        // Only SaveChanges failures (which EF wraps in DbUpdateException) can be about the data; a read
        // that fails says something about the database. SaveAsync writes only through SaveChangesAsync (no
        // ExecuteUpdate or raw write; its only other statements are the people lock and its transaction, which
        // fail only for reasons that are not the manifest's), so no data error escapes unwrapped; a raw write
        // added there needs its own catch.
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && IsDataError(ex.InnerException)
            // In queue mode a race with another import is not this manifest's fault: the event comes back.
            && !(transientFailuresThrow && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }))
        {
            return (false, $"could not be saved: {Describe(ex.InnerException!)}");
        }
        finally
        {
            // Whatever happened, the next manifest starts from a clean context.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Names the asset's place (S-012) when its location is new or changed, or it has none yet; a re-import of
    /// the same location keeps the place without a query. Without GeoNames loaded no place is found, and
    /// <c>places refresh</c> names the asset after the load.
    /// </summary>
    private async Task NamePlaceAsync(Asset asset, Point? location, CancellationToken cancellationToken)
    {
        if (location is null)
        {
            SetPlace(asset, null);
            return;
        }

        if (asset.PlaceCity is not null && asset.Location is { } previous && previous.EqualsExact(location))
        {
            return;
        }

        SetPlace(asset, await places.ResolveAsync(location, cancellationToken));
    }

    private static void SetPlace(Asset asset, Place? place)
    {
        asset.PlaceCity = place?.City;
        asset.PlaceRegion = place?.Region;
        asset.PlaceCountry = place?.Country;
    }

    private async Task SaveAsync(
        Manifest manifest, string kind, IReadOnlyDictionary<string, float[]> embeddings, IReadOnlyList<IncomingFace>? faces,
        string blobName, string etag, CancellationToken cancellationToken)
    {
        // A manifest with faces is saved in a transaction that holds the people lock from before its faces are read
        // until its people are saved, so imports running at once assign faces one after the other (D-077).
        await using var transaction = faces is null ? null : await db.Database.BeginTransactionAsync(cancellationToken);
        if (transaction is not null)
        {
            await FaceAssigner.LockAsync(db, cancellationToken);
            // Read under the lock, so turning recognition off (which takes it too) either happened before or waits
            // (S-022, D-081): while it is off, and for faces found before the last erase, the manifest's faces are
            // ignored as if it had not looked for them.
            if (!(await FaceRecognition.ReadAsync(db, cancellationToken)).Accepts(manifest.FacesDetectedAt))
            {
                faces = null;
            }
        }

        var now = clock.GetUtcNow();
        var asset = await db.Assets
            .Include(a => a.Sources)
            .Include(a => a.Derivatives)
            .Include(a => a.Albums)
            .SingleOrDefaultAsync(a => a.Id == manifest.Id, cancellationToken);
        if (asset is null)
        {
            asset = new Asset { Id = manifest.Id, Kind = kind, ContentType = manifest.ContentType, ImportedAt = now, OriginalStored = manifest.IsOriginalStored };
            db.Assets.Add(asset);
        }
        else
        {
            // Stored once any manifest says so, and then it stays (S-063, D-088): an index-only manifest never takes it back.
            asset.OriginalStored |= manifest.IsOriginalStored;
        }

        asset.Kind = kind;
        asset.SizeBytes = manifest.SizeBytes;
        asset.ContentType = manifest.ContentType;
        // Wall-clock time from the camera (the schema refuses offsets): stored exactly as written.
        asset.CapturedAt = manifest.CapturedAt is { } captured ? DateTime.SpecifyKind(captured, DateTimeKind.Unspecified) : null;
        asset.CapturedAtSource = manifest.CapturedAtSource is { } dateSource ? System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(dateSource.ToString()) : null;
        var location = manifest.Location is { } at ? s_wgs84.CreatePoint(new Coordinate(at.Longitude, at.Latitude)) : null;
        await NamePlaceAsync(asset, location, cancellationToken);
        asset.Location = location;
        asset.CameraMake = manifest.Camera?.Make;
        asset.CameraModel = manifest.Camera?.Model;
        asset.Width = manifest.Width;
        asset.Height = manifest.Height;
        asset.DurationSeconds = manifest.DurationSeconds;
        // A manifest written before the photo was measured has none: what is stored stays (S-027, D-098).
        if (manifest.Quality is { } quality)
        {
            (asset.Sharpness, asset.DHash) = (quality.Sharpness, quality.DHash);
        }

        // A manifest without a caption leaves a stored one as it is (S-052, D-114), as one without albums leaves the albums.
        asset.Caption = manifest.Caption ?? asset.Caption;
        foreach (var album in (manifest.Albums ?? []).Select(AlbumName).OfType<string>())
        {
            if (asset.Albums.All(a => a.Name != album))
            {
                asset.Albums.Add(new AssetAlbum { AssetId = asset.Id, Name = album });
            }
        }

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
                    Url = source.Url,
                });
            }
            else
            {
                existing.ModifiedAt = modifiedAt;
                // The merged manifest keeps a url that any side had, so an absent one only means none was ever given.
                existing.Url = source.Url ?? existing.Url;
            }
        }

        // Derivatives describe the files in storage now, so the manifest replaces them.
        asset.Derivatives.RemoveAll(d => manifest.Derivatives.All(m => m.Name != d.Name));
        foreach (var derivative in manifest.Derivatives)
        {
            var blobPath = BlobLayout.DerivativeBlobName(asset.Id, derivative.Path);
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

        // Faces only when the manifest looked for them: false means "not looked for", never "no faces" (D-047), so the
        // stored faces and their people stay. Faces of an older pipeline never replace a newer one's.
        if (faces is not null
            && await _faces.ApplyAsync(asset.Id, asset.FacesVersion, manifest.EffectiveFacesVersion, faces, cancellationToken))
        {
            asset.FacesVersion = manifest.EffectiveFacesVersion;
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
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The embeddings the catalog stores, by model key, with the dimensions of their column. Keys of other
    /// models are left in the manifest for a later story to import.
    /// </summary>
    private static readonly Dictionary<string, int> s_embeddingDimensions = new(StringComparer.Ordinal)
    {
        [ClipImageEmbedder.ModelKey] = ClipImageEmbedder.Dimensions,
    };

    /// <summary>
    /// The embeddings to store. The schema has already checked their keys and base64 length; a vector whose values
    /// are not finite or that has no length would break cosine search, so it is left out and reported, and the
    /// asset is imported without it (D-043).
    /// </summary>
    private static (IReadOnlyDictionary<string, float[]> Embeddings, string? Problem) ReadEmbeddings(Manifest manifest)
    {
        var read = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var problems = new List<string>();
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
                problems.Add($"has an unreadable {model} embedding, which was left out: {ex.Message}");
                continue;
            }

            if (values.Length != dimensions || values.Any(v => !float.IsFinite(v)) || values.All(v => v == 0))
            {
                problems.Add($"has a {model} embedding that is not {dimensions} finite values with a length, which was left out");
                continue;
            }

            read[model] = values;
        }

        return (read, problems.Count == 0 ? null : string.Join("; ", problems));
    }

    /// <summary>
    /// The faces to store, or null when the manifest did not look for any. The schema has already checked each face's
    /// shape; a face whose embedding is not 128 finite values with a length, or whose box is not four finite values,
    /// would break cosine grouping, so it is left out and the manifest listed once in the warnings (D-043, D-047).
    /// </summary>
    private static (IReadOnlyList<IncomingFace>? Faces, string? Problem) ReadFaces(Manifest manifest)
    {
        if (!manifest.FacesProcessed)
        {
            return (null, null);
        }

        var read = new List<IncomingFace>();
        var leftOut = 0;
        foreach (var face in manifest.Faces)
        {
            float[] values;
            try
            {
                values = EmbeddingCodec.Decode(face.Embedding);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                leftOut++;
                continue;
            }

            if (values.Length != FaceDetector.Dimensions || values.Any(v => !float.IsFinite(v)) || values.All(v => v == 0)
                || face.Box.Count != 4 || face.Box.Any(v => !double.IsFinite(v)) || !double.IsFinite(face.Score))
            {
                leftOut++;
                continue;
            }

            read.Add(new IncomingFace([.. face.Box], face.Score, values));
        }

        return (read, leftOut == 0
            ? null
            : $"has {leftOut} face{(leftOut == 1 ? string.Empty : "s")} whose embedding is not {FaceDetector.Dimensions} finite values with a length or whose box is not four finite values, left out");
    }

    /// <summary>
    /// An album's name as the catalog keeps it (S-052, D-114): cut to the 200 characters the search takes (a longer name could not be filtered
    /// on, and the key's index refuses a row over about 2.7 KB, which would fail the whole manifest), not in the middle of a character, and
    /// without control characters; null when nothing is left.
    /// </summary>
    internal static string? AlbumName(string name)
    {
        const int Max = Sepiabox.Catalog.Search.SearchQueryJson.MaxNameLength;
        var clean = new string([.. name.Where(c => !char.IsControl(c) && !Sepiabox.Catalog.Search.InvisibleText.IsInvisible(c))]).Trim();
        if (clean.Length > Max)
        {
            clean = clean[..(char.IsHighSurrogate(clean[Max - 1]) ? Max - 1 : Max)].TrimEnd();
        }

        return clean.Length == 0 ? null : clean;
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
}
