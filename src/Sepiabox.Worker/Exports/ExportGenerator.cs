using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Storage;

namespace Sepiabox.Worker.Exports;

/// <summary>
/// <c>export</c> (S-040, D-109): writes the zip each pending <see cref="ExportRequest"/> asks for. The job runs on a schedule
/// and does nothing, in one query, while no one has asked. A zip holds <c>catalog.json</c> (assets, their sources with
/// their addresses, people with names, occasions and personal places), <c>originals.txt</c> (one line for each original
/// Sepiabox stores, with a link that works for 24 hours) and <c>README.txt</c>; the faces (boxes and embeddings, which are
/// biometric data) only when the request asked. The originals themselves are not copied into it.
/// </summary>
/// <param name="db">The catalog.</param>
/// <param name="store">The blobs; its read links must be allowed to last <see cref="OriginalLinkLifetime"/>.</param>
/// <param name="clock">Dates the zip and the links.</param>
/// <param name="logger">Reports each export and its failures.</param>
public sealed class ExportGenerator(CatalogDbContext db, IBlobStore store, TimeProvider clock, ILogger logger)
{
    /// <summary>How long the links in <c>originals.txt</c> work.</summary>
    public static readonly TimeSpan OriginalLinkLifetime = TimeSpan.FromHours(24);

    /// <summary>The container the zips are written to.</summary>
    public const string Container = "exports";

    /// <summary>The session lock that keeps two export runs (a long one and the next schedule) from running at once.</summary>
    public const long ExportLockKey = 0x5E91AB21;

    /// <summary>A request still running after this long was cut off (a restart, a timeout): it is failed, and can be asked again.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);

    private const int PageSize = 500;

    /// <summary>Writes every pending export; the number written.</summary>
    public async Task<int> RunPendingAsync(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({ExportLockKey}) AS \"Value\"").SingleAsync(cancellationToken))
            {
                Log.ExportBusy(logger);
                return 0;
            }

            try
            {
                await FailStaleAsync(cancellationToken);
                var done = 0;
                while (await ClaimAsync(cancellationToken) is { } request)
                {
                    done += await WriteAsync(request, cancellationToken) ? 1 : 0;
                }

                return done;
            }
            finally
            {
                await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({ExportLockKey}) AS \"Value\"").SingleAsync(CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task FailStaleAsync(CancellationToken cancellationToken)
    {
        var limit = clock.GetUtcNow() - StaleAfter;
        foreach (var stale in await db.ExportRequests.Where(e => e.Status == ExportStatuses.Running && e.StartedAt < limit).ToListAsync(cancellationToken))
        {
            stale.Status = ExportStatuses.Failed;
            stale.FinishedAt = clock.GetUtcNow();
            stale.Error = "The export was interrupted. Ask for it again.";
            Log.ExportInterrupted(logger, stale.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ExportRequest?> ClaimAsync(CancellationToken cancellationToken)
    {
        var request = await db.ExportRequests.Where(e => e.Status == ExportStatuses.Requested).OrderBy(e => e.Id).FirstOrDefaultAsync(cancellationToken);
        if (request is null)
        {
            return null;
        }

        request.Status = ExportStatuses.Running;
        request.StartedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    private async Task<bool> WriteAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        var name = $"{clock.GetUtcNow():yyyy-MM-dd}-{request.Id}.zip";
        try
        {
            await using var upload = store.BeginUpload(Container, name);
            int assets;
            long size;
            await using (var sink = new StagedUploadStream(upload))
            {
                assets = await WriteZipAsync(sink, request.IncludeFaceData, cancellationToken);
                await sink.FinishAsync(cancellationToken);
                size = sink.Length;
            }

            await upload.CommitAsync("application/zip", BlobStorageClass.Default, cancellationToken);
            request.Status = ExportStatuses.Done;
            request.BlobName = name;
            request.Assets = assets;
            request.SizeBytes = size;
            request.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            Log.ExportWritten(logger, request.Id, name, assets, size);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.ExportFailed(logger, request.Id, ex);
            db.ChangeTracker.Clear();
            var failed = await db.ExportRequests.SingleAsync(e => e.Id == request.Id, CancellationToken.None);
            failed.Status = ExportStatuses.Failed;
            failed.FinishedAt = clock.GetUtcNow();
            failed.Error = $"The export could not be written ({ex.GetType().Name}). Ask for it again.";
            await db.SaveChangesAsync(CancellationToken.None);
            return false;
        }
    }

    // The zip's entries, one after the other: only one can be open at a time.
    private async Task<int> WriteZipAsync(Stream sink, bool includeFaces, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        using var zip = new ZipArchive(sink, ZipArchiveMode.Create, leaveOpen: true);
        var readme = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
        await using (var stream = readme.Open())
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(Readme(now, includeFaces)), cancellationToken);
        }

        var catalog = zip.CreateEntry("catalog.json", CompressionLevel.Optimal);
        int assets;
        await using (var stream = catalog.Open())
        {
            assets = await WriteCatalogAsync(stream, now, includeFaces, cancellationToken);
        }

        var originals = zip.CreateEntry("originals.txt", CompressionLevel.Optimal);
        await using (var stream = originals.Open())
        {
            await WriteOriginalsAsync(stream, now, cancellationToken);
        }

        return assets;
    }

    private async Task<int> WriteCatalogAsync(Stream stream, DateTimeOffset now, bool includeFaces, CancellationToken cancellationToken)
    {
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("exportedAt", now);
        writer.WriteString("format", "sepiabox-export-1");
        writer.WriteBoolean("includesFaceData", includeFaces);

        writer.WriteStartArray("assets");
        var count = 0;
        var last = "";
        while (true)
        {
            var page = await db.Assets.AsNoTracking().Include(a => a.Sources)
                .Where(a => a.Id.CompareTo(last) > 0).OrderBy(a => a.Id).Take(PageSize).ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            var faces = await FacesOfAsync(page.Select(a => a.Id).ToList(), includeFaces, cancellationToken);
            foreach (var asset in page)
            {
                WriteAsset(writer, asset, faces.GetValueOrDefault(asset.Id) ?? [], includeFaces);
                count++;
            }

            last = page[^1].Id;
        }

        writer.WriteEndArray();

        writer.WriteStartArray("people");
        foreach (var person in await db.People.AsNoTracking().OrderBy(p => p.Id).ToListAsync(cancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", person.Id);
            writer.WriteString("name", person.Name);
            writer.WriteString("linkedUser", person.UserEmail);
            writer.WriteNumber("faceCount", person.FaceCount);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        var occasionAssets = (await db.OccasionAssets.AsNoTracking().OrderBy(o => o.AssetId).Select(o => new { o.EventId, o.AssetId }).ToListAsync(cancellationToken))
            .GroupBy(o => o.EventId).ToDictionary(g => g.Key, g => g.Select(o => o.AssetId).ToList());
        writer.WriteStartArray("occasions");
        foreach (var occasion in await db.Occasions.AsNoTracking().OrderBy(o => o.StartsAt).ThenBy(o => o.Id).ToListAsync(cancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", occasion.Id);
            writer.WriteString("type", occasion.Type);
            writer.WriteString("startsAt", occasion.StartsAt);
            writer.WriteString("endsAt", occasion.EndsAt);
            writer.WriteString("place", occasion.PlaceLabel);
            writer.WriteStartArray("assets");
            foreach (var id in occasionAssets.GetValueOrDefault(occasion.Id) ?? [])
            {
                writer.WriteStringValue(id);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("savedPlaces");
        foreach (var place in await db.SavedPlaces.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteString("name", place.Name);
            writer.WriteNumber("latitude", place.Location.Y);
            writer.WriteNumber("longitude", place.Location.X);
            writer.WriteNumber("radiusMetres", place.RadiusM);
            writer.WriteBoolean("home", place.IsHome);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
        return count;
    }

    private sealed record FaceRow(string AssetId, double[] Box, double Score, string Assignment, long? PersonId, string? PersonName, float[]? Embedding);

    private async Task<Dictionary<string, List<FaceRow>>> FacesOfAsync(List<string> assetIds, bool includeFaces, CancellationToken cancellationToken)
    {
        // The embedding is read only when it is asked for: it is the biometric part.
        var rows = includeFaces
            ? (await db.Faces.AsNoTracking().Where(f => assetIds.Contains(f.AssetId))
                .Select(f => new { f.AssetId, f.Box, f.Score, f.Assignment, f.PersonId, Name = f.Person != null ? f.Person.Name : null, f.Embedding })
                .ToListAsync(cancellationToken))
                .Select(f => new FaceRow(f.AssetId, f.Box, f.Score, f.Assignment, f.PersonId, f.Name, f.Embedding.ToArray()))
            : (await db.Faces.AsNoTracking().Where(f => assetIds.Contains(f.AssetId))
                .Select(f => new { f.AssetId, f.Box, f.Score, f.Assignment, f.PersonId, Name = f.Person != null ? f.Person.Name : null })
                .ToListAsync(cancellationToken))
                .Select(f => new FaceRow(f.AssetId, f.Box, f.Score, f.Assignment, f.PersonId, f.Name, null));
        return rows.GroupBy(f => f.AssetId).ToDictionary(g => g.Key, g => g.OrderBy(f => f.Box[0]).ToList());
    }

    private static void WriteAsset(Utf8JsonWriter writer, Asset asset, List<FaceRow> faces, bool includeFaces)
    {
        writer.WriteStartObject();
        writer.WriteString("id", asset.Id);
        writer.WriteString("kind", asset.Kind);
        writer.WriteString("contentType", asset.ContentType);
        writer.WriteNumber("sizeBytes", asset.SizeBytes);
        if (asset.CapturedAt is { } captured)
        {
            writer.WriteString("capturedAt", captured);
        }

        if (asset.Location is { } location)
        {
            writer.WriteStartObject("location");
            writer.WriteNumber("latitude", location.Y);
            writer.WriteNumber("longitude", location.X);
            writer.WriteEndObject();
        }

        if (asset.CameraMake is not null || asset.CameraModel is not null)
        {
            writer.WriteStartObject("camera");
            writer.WriteString("make", asset.CameraMake);
            writer.WriteString("model", asset.CameraModel);
            writer.WriteEndObject();
        }

        if (asset.Width is { } width)
        {
            writer.WriteNumber("width", width);
        }

        if (asset.Height is { } height)
        {
            writer.WriteNumber("height", height);
        }

        if (asset.DurationSeconds is { } duration)
        {
            writer.WriteNumber("durationSeconds", duration);
        }

        if (asset.PlaceCity is not null || asset.PlaceRegion is not null || asset.PlaceCountry is not null)
        {
            writer.WriteStartObject("place");
            writer.WriteString("city", asset.PlaceCity);
            writer.WriteString("region", asset.PlaceRegion);
            writer.WriteString("country", asset.PlaceCountry);
            writer.WriteEndObject();
        }

        // Says where every original is: stored by Sepiabox (and in originals.txt), or left at its source (D-088).
        writer.WriteBoolean("originalStored", asset.OriginalStored);
        writer.WriteBoolean("hidden", asset.Hidden);
        writer.WriteString("importedAt", asset.ImportedAt);

        writer.WriteStartArray("sources");
        foreach (var source in asset.Sources.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Path, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("type", source.Type);
            writer.WriteString("name", source.Name);
            writer.WriteString("path", source.Path);
            writer.WriteString("modifiedAt", source.ModifiedAt);
            if (source.Url is not null)
            {
                writer.WriteString("url", source.Url);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        // The names of the people in the photo (faces that count, D-077); no biometric data.
        writer.WriteStartArray("people");
        foreach (var name in faces.Where(f => FaceAssignments.Counts(f.Assignment) && f.PersonName is not null).Select(f => f.PersonName!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(name);
        }

        writer.WriteEndArray();

        if (includeFaces)
        {
            writer.WriteStartArray("faces");
            foreach (var face in faces)
            {
                writer.WriteStartObject();
                writer.WriteStartArray("box");
                foreach (var value in face.Box)
                {
                    writer.WriteNumberValue(value);
                }

                writer.WriteEndArray();
                writer.WriteNumber("score", face.Score);
                writer.WriteString("assignment", face.Assignment);
                if (face.PersonId is { } personId)
                {
                    writer.WriteNumber("personId", personId);
                }

                writer.WriteStartArray("embedding");
                foreach (var value in face.Embedding ?? [])
                {
                    writer.WriteNumberValue(value);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    // One line for each original Sepiabox stores: asset id, a file name to save it as, and a link good for 24 hours.
    private async Task WriteOriginalsAsync(Stream stream, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expires = now + OriginalLinkLifetime;
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };
        await writer.WriteLineAsync("# One line for each original Sepiabox stores: asset id, TAB, a file name to save it as, TAB, a link.");
        await writer.WriteLineAsync($"# The links work until {expires.UtcDateTime:yyyy-MM-dd HH:mm} UTC. Ask for a new export for new links.");
        await writer.WriteLineAsync("# Originals left at their source (index-only, reference mode) are not listed: catalog.json has their addresses.");
        var last = "";
        while (true)
        {
            var page = await db.Assets.AsNoTracking().Include(a => a.Sources)
                .Where(a => a.OriginalStored && a.Id.CompareTo(last) > 0).OrderBy(a => a.Id).Take(PageSize).ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var asset in page)
            {
                var fileName = FileNameOf(asset);
                var link = await store.CreateReadUrlAsync(
                    BlobLayout.OriginalsContainer, asset.Id, expires, new BlobReadUrlOptions(DownloadFileName: fileName), cancellationToken);
                await writer.WriteLineAsync($"{asset.Id}\t{fileName}\t{link.AbsoluteUri}");
            }

            last = page[^1].Id;
        }

        await writer.FlushAsync(cancellationToken);
    }

    // The first source's file name, with its folder dropped and a tab or line break (which would end the field) turned into a space.
    private static string FileNameOf(Asset asset)
    {
        var path = asset.Sources.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Path, StringComparer.Ordinal).FirstOrDefault()?.Path ?? asset.Id;
        var name = path.Replace('\\', '/')[(path.Replace('\\', '/').LastIndexOf('/') + 1)..];
        var clean = new string([.. name.Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        return clean.Length == 0 ? asset.Id : clean;
    }

    private static string Readme(DateTimeOffset now, bool includeFaces) => $"""
        Sepiabox export, {now.UtcDateTime:yyyy-MM-dd HH:mm} UTC
        =====================================================

        Your photos and videos are yours. This zip is everything Sepiabox knows about them, in open formats.

        catalog.json   Every photo and video: its date, place, camera, size, where each copy came from
                       (sources: drive or account, path, address when it has one), the names of the people in it,
                       whether Sepiabox stores its original (originalStored). Then the people with their names,
                       the occasions, and your personal places. Plain JSON, UTF-8.
        originals.txt  One line for each original Sepiabox stores: the asset id, a file name, and a link, separated
                       by tabs. The links work for 24 hours: download with any tool, for example
                       `while IFS=$'\t' read -r id name url; do curl -L -o "$name" "$url"; done < <(grep -v '^#' originals.txt)`.
                       Originals that stay at their source are not listed; catalog.json says where they are.
        README.txt     This file.

        {(includeFaces ? "Face data is included: each photo's faces have a box, a score and the embedding the app compares faces with. It is biometric data; keep this zip private." : "Face data (face boxes and embeddings) is not included. Ask for an export with face data on the settings page if you want it.")}
        The search embeddings of the photos are never included: they are recomputed from the pictures.

        ---

        Exportação do Sepiabox, {now.UtcDateTime:yyyy-MM-dd HH:mm} UTC

        Suas fotos e vídeos são seus. Este zip tem tudo o que o Sepiabox sabe sobre eles, em formatos abertos.

        catalog.json   Cada foto e vídeo: data, local, câmera, tamanho, de onde veio cada cópia (origens: unidade ou
                       conta, caminho, endereço quando tem), os nomes das pessoas, e se o Sepiabox guarda o original
                       (originalStored). Depois as pessoas com seus nomes, as ocasiões e seus lugares.
        originals.txt  Uma linha por original guardado pelo Sepiabox: o id, um nome de arquivo e um link, separados
                       por tabulações. Os links funcionam por 24 horas. Os originais que ficam na origem não
                       aparecem; o catalog.json diz onde estão.
        {(includeFaces ? "Os dados de rostos estão incluídos (caixa, pontuação e embedding): são dados biométricos; guarde este zip em privado." : "Os dados de rostos (caixas e embeddings) não estão incluídos. Peça uma exportação com dados de rostos nas configurações se quiser.")}
        """;
}
