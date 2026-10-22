using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Pgvector;
using Sepiabox.Catalog;
using Sepiabox.Storage;
using Sepiabox.Storage.Azure;
using Sepiabox.Worker.Exports;

namespace Sepiabox.Worker.Tests.Exports;

/// <summary>
/// <c>export</c> on PostgreSQL and Azurite (S-040, D-110): the zip's contents, the faces only when asked, the links' expiry,
/// and what becomes of a request that fails.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ExportGeneratorTests(CatalogFixture fixture)
{
    private static readonly DateTimeOffset s_now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task The_catalog_has_every_asset_with_its_sources_people_occasions_and_places_and_no_faces_by_default()
    {
        await using var db = await SeedAsync();
        var request = await RequestAsync(db, includeFaces: false);

        var written = await Generator(db).RunPendingAsync(CancellationToken.None);

        Assert.Equal(1, written);
        var (catalog, _, _) = await ReadZipAsync(db, request);
        using var json = JsonDocument.Parse(catalog);
        var assets = json.RootElement.GetProperty("assets");
        Assert.Equal(["a1", "a2", "a3"], assets.EnumerateArray().Select(a => a.GetProperty("id").GetString()));
        var first = assets[0];
        Assert.Equal(("photo", 2000), (first.GetProperty("kind").GetString(), first.GetProperty("sizeBytes").GetInt32()));
        Assert.Equal(38.7, first.GetProperty("location").GetProperty("latitude").GetDouble(), 3);
        Assert.Equal("Lisbon", first.GetProperty("place").GetProperty("city").GetString());
        Assert.Equal(["HD da Ana", "Celular"], first.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("name").GetString()).OrderDescending());
        Assert.Equal(["Ana"], first.GetProperty("people").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("Ana", json.RootElement.GetProperty("people")[0].GetProperty("name").GetString());
        Assert.False(json.RootElement.GetProperty("includesFaceData").GetBoolean());
        Assert.Equal("Casa da praia", json.RootElement.GetProperty("savedPlaces")[0].GetProperty("name").GetString());
        Assert.Equal(["a1", "a2"], json.RootElement.GetProperty("occasions")[0].GetProperty("assets").EnumerateArray().Select(a => a.GetString()));
        // No biometric data, nor any embedding, anywhere in the file.
        Assert.DoesNotContain("embedding", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"faces\"", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("\"box\"", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Face_data_is_in_the_catalog_only_when_the_request_asked_for_it()
    {
        await using var db = await SeedAsync();
        var request = await RequestAsync(db, includeFaces: true);

        await Generator(db).RunPendingAsync(CancellationToken.None);

        var (catalog, _, _) = await ReadZipAsync(db, request);
        using var json = JsonDocument.Parse(catalog);
        Assert.True(json.RootElement.GetProperty("includesFaceData").GetBoolean());
        var face = Assert.Single(json.RootElement.GetProperty("assets")[0].GetProperty("faces").EnumerateArray());
        Assert.Equal(128, face.GetProperty("embedding").GetArrayLength());
        Assert.Equal(4, face.GetProperty("box").GetArrayLength());
        Assert.Equal("auto", face.GetProperty("assignment").GetString());
    }

    [Fact]
    public async Task Originals_txt_lists_the_stored_originals_only_each_with_a_link_that_ends_within_24_hours()
    {
        await using var db = await SeedAsync();
        var request = await RequestAsync(db, includeFaces: false);

        await Generator(db).RunPendingAsync(CancellationToken.None);

        var (catalog, originals, _) = await ReadZipAsync(db, request);
        var lines = originals.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).ToList();
        // a3 is index-only (D-088): no line, and the catalog says where its original is.
        Assert.Equal(["a1", "a2"], lines.Select(l => l.Split('\t')[0]));
        Assert.Equal(["IMG_1.jpg", "a2.mov"], lines.Select(l => l.Split('\t')[1]));
        foreach (var line in lines)
        {
            var url = new Uri(line.Split('\t')[2]);
            var expires = DateTimeOffset.Parse(Regex.Match(Uri.UnescapeDataString(url.Query), "se=([^&]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(expires <= DateTimeOffset.UtcNow.AddHours(24).AddMinutes(1), expires.ToString("O"));
            Assert.True(expires >= DateTimeOffset.UtcNow.AddHours(23), expires.ToString("O"));
            Assert.Contains("/originals/", url.AbsolutePath, StringComparison.Ordinal);
        }

        using var json = JsonDocument.Parse(catalog);
        var indexOnly = json.RootElement.GetProperty("assets")[2];
        Assert.False(indexOnly.GetProperty("originalStored").GetBoolean());
        Assert.Equal("https://drive.example.com/file/3", indexOnly.GetProperty("sources")[0].GetProperty("url").GetString());
        Assert.True(json.RootElement.GetProperty("assets")[0].GetProperty("originalStored").GetBoolean());
    }

    [Fact]
    public async Task Records_the_zip_its_size_and_the_asset_count_on_the_request_and_readme_names_what_it_holds()
    {
        await using var db = await SeedAsync();
        var request = await RequestAsync(db, includeFaces: false);

        await Generator(db).RunPendingAsync(CancellationToken.None);

        var (_, _, readme) = await ReadZipAsync(db, request);
        var done = await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == request.Id);
        Assert.Equal((ExportStatuses.Done, 3), (done.Status, done.Assets));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}-\d+\.zip$", done.BlobName);
        Assert.True(done.SizeBytes > 100);
        Assert.NotNull(done.FinishedAt);
        Assert.Contains("catalog.json", readme, StringComparison.Ordinal);
        Assert.Contains("originals.txt", readme, StringComparison.Ordinal);
        Assert.Contains("Exportação", readme, StringComparison.Ordinal);
        Assert.Contains("not included", readme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_readme_warns_in_both_languages_that_the_links_in_the_zip_share_the_library_for_24_hours()
    {
        await using var db = await SeedAsync();
        var request = await RequestAsync(db, includeFaces: false);
        await Generator(db).RunPendingAsync(CancellationToken.None);

        var (_, _, readme) = await ReadZipAsync(db, request);

        Assert.Contains("Passing this zip on in that time shares the whole library", readme, StringComparison.Ordinal);
        Assert.Contains("compartilha a biblioteca inteira", readme, StringComparison.Ordinal);
        Assert.Contains("deleted after 7 days", readme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turning_face_recognition_off_deletes_the_zips_that_have_face_data_and_keeps_the_others()
    {
        await using var db = await SeedAsync();
        var withFaces = await RequestAsync(db, includeFaces: true);
        await Generator(db).RunPendingAsync(CancellationToken.None);
        var plain = await RequestAsync(db, includeFaces: false);
        await Generator(db).RunPendingAsync(CancellationToken.None);
        var faceBlob = (await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == withFaces.Id)).BlobName!;
        var plainBlob = (await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == plain.Id)).BlobName!;
        Assert.NotNull(await fixture.Store.ReadAsync("exports", faceBlob));

        // While recognition is on, nothing is deleted.
        await Generator(db).RunPendingAsync(CancellationToken.None);
        Assert.NotNull(await fixture.Store.ReadAsync("exports", faceBlob));

        db.Settings.Add(new Setting { Key = Sepiabox.Catalog.People.FaceRecognition.RecognitionKey, Value = "off" });
        await db.SaveChangesAsync();
        await Generator(db).RunPendingAsync(CancellationToken.None);

        Assert.Null(await fixture.Store.ReadAsync("exports", faceBlob));
        Assert.NotNull(await fixture.Store.ReadAsync("exports", plainBlob));
        db.ChangeTracker.Clear();
        Assert.Null((await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == withFaces.Id)).BlobName);
        Assert.Equal(plainBlob, (await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == plain.Id)).BlobName);
    }

    [Fact]
    public async Task Does_nothing_when_no_one_asked()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();

        Assert.Equal(0, await Generator(db).RunPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_write_is_recorded_without_a_trace_and_the_next_request_still_runs()
    {
        await using var db = await SeedAsync();
        var failing = await RequestAsync(db, includeFaces: false);
        var store = new FailOnceStore(new AzureBlobStore(fixture.Blobs, TimeProvider.System, ExportGenerator.OriginalLinkLifetime));
        var generator = new ExportGenerator(db, store, TimeProvider.System, NullLogger.Instance);

        Assert.Equal(0, await generator.RunPendingAsync(CancellationToken.None));
        // Only one request is active at a time (the unique index), so the next is asked once the failed one has ended.
        var good = await RequestAsync(db, includeFaces: false);
        var written = await generator.RunPendingAsync(CancellationToken.None);

        Assert.Equal(1, written);
        db.ChangeTracker.Clear();
        var failedRow = await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == failing.Id);
        Assert.Equal(ExportStatuses.Failed, failedRow.Status);
        Assert.Contains("Ask for it again", failedRow.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("at ", failedRow.Error, StringComparison.Ordinal);
        Assert.Equal(ExportStatuses.Done, (await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == good.Id)).Status);
    }

    [Fact]
    public async Task A_request_left_running_for_hours_is_failed_as_interrupted_and_a_recent_one_is_left_running()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        db.ExportRequests.Add(new ExportRequest
        {
            RequestedAt = s_now.AddHours(-5),
            RequestedBy = "ana@example.com",
            Status = ExportStatuses.Running,
            StartedAt = s_now.AddHours(-4),
        });
        await db.SaveChangesAsync();

        await Generator(db).RunPendingAsync(CancellationToken.None);

        var stale = await db.ExportRequests.AsNoTracking().SingleAsync();
        Assert.Equal(ExportStatuses.Failed, stale.Status);
        Assert.Contains("interrupted", stale.Error, StringComparison.Ordinal);

        // One that started minutes ago may well be running (another run of the job holds the lock): it is left alone.
        db.ExportRequests.Add(new ExportRequest
        {
            RequestedAt = s_now.AddMinutes(-5),
            RequestedBy = "ana@example.com",
            Status = ExportStatuses.Running,
            StartedAt = s_now.AddMinutes(-2),
        });
        await db.SaveChangesAsync();

        await Generator(db).RunPendingAsync(CancellationToken.None);

        Assert.Equal([ExportStatuses.Failed, ExportStatuses.Running], (await db.ExportRequests.AsNoTracking().OrderBy(e => e.Id).ToListAsync()).Select(r => r.Status));
    }

    [Fact]
    public async Task A_finished_or_failed_request_that_started_long_ago_is_left_exactly_as_it_was()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var done = new ExportRequest
        {
            RequestedAt = s_now.AddHours(-9),
            RequestedBy = "ana@example.com",
            Status = ExportStatuses.Done,
            StartedAt = s_now.AddHours(-8),
            FinishedAt = s_now.AddHours(-7),
            BlobName = "2026-10-01-1.zip",
            SizeBytes = 1234,
            Assets = 3,
        };
        var failed = new ExportRequest
        {
            RequestedAt = s_now.AddHours(-6),
            RequestedBy = "ana@example.com",
            Status = ExportStatuses.Failed,
            StartedAt = s_now.AddHours(-5),
            FinishedAt = s_now.AddHours(-4),
            Error = "The export could not be written (IOException). Ask for it again.",
        };
        db.ExportRequests.AddRange(done, failed);
        await db.SaveChangesAsync();

        await Generator(db).RunPendingAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var rows = await db.ExportRequests.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal([ExportStatuses.Done, ExportStatuses.Failed], rows.Select(r => r.Status));
        Assert.Equal(("2026-10-01-1.zip", 1234L, 3, null), (rows[0].BlobName, rows[0].SizeBytes, rows[0].Assets, rows[0].Error));
        Assert.Equal(done.FinishedAt!.Value.ToUnixTimeMilliseconds(), rows[0].FinishedAt!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(failed.Error, rows[1].Error);
        Assert.Equal(failed.FinishedAt!.Value.ToUnixTimeMilliseconds(), rows[1].FinishedAt!.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Does_nothing_and_changes_nothing_while_another_run_holds_the_export_lock()
    {
        await using var db = await SeedAsync();
        var request = await RequestAsync(db, includeFaces: false);
        await using var other = new Npgsql.NpgsqlConnection(fixture.ConnectionStringFor(db));
        await other.OpenAsync();
        await using (var lockCommand = new Npgsql.NpgsqlCommand($"SELECT pg_advisory_lock({ExportGenerator.ExportLockKey})", other))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        var before = (await fixture.Store.ListAsync("exports").ToListAsync()).Select(b => b.Name).Order().ToList();

        var written = await Generator(db).RunPendingAsync(CancellationToken.None);

        Assert.Equal(0, written);
        db.ChangeTracker.Clear();
        var row = await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == request.Id);
        Assert.Equal((ExportStatuses.Requested, null, null), (row.Status, row.StartedAt, row.BlobName));
        Assert.Equal(before, (await fixture.Store.ListAsync("exports").ToListAsync()).Select(b => b.Name).Order());
    }

    [Fact]
    public async Task The_error_stored_for_a_failed_write_names_the_exception_type_and_nothing_of_its_message()
    {
        await using var db = await SeedAsync();
        var failing = await RequestAsync(db, includeFaces: false);
        var store = new ThrowingStore(new AzureBlobStore(fixture.Blobs, TimeProvider.System, ExportGenerator.OriginalLinkLifetime), new IOException("cannot write /secret/library/path/zip.tmp for ana@example.com"));

        await new ExportGenerator(db, store, TimeProvider.System, NullLogger.Instance).RunPendingAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var error = (await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == failing.Id)).Error!;
        Assert.Contains("IOException", error, StringComparison.Ordinal);
        Assert.DoesNotContain("/secret", error, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@example.com", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_catalog_counts_only_faces_that_count_names_each_person_once_orders_faces_by_position_and_sources_by_name_and_says_hidden_and_home()
    {
        await using var db = await SeedAsync();
        var bruno = new Person { Name = "Bruno", UserEmail = null, FaceCount = 0 };
        var carla = new Person { Name = "Carla", UserEmail = null, FaceCount = 0 };
        db.People.AddRange(bruno, carla);
        db.Assets.Add(new Asset
        {
            Id = "a4",
            Kind = "photo",
            ContentType = "image/jpeg",
            SizeBytes = 10,
            Hidden = true,
            ImportedAt = s_now,
            UpdatedAt = s_now,
            Sources =
            [
                new AssetSource { AssetId = "a4", Type = "local", Name = "Zeta", Path = "b.jpg", ModifiedAt = s_now },
                new AssetSource { AssetId = "a4", Type = "local", Name = "Alfa", Path = "z.jpg", ModifiedAt = s_now },
                new AssetSource { AssetId = "a4", Type = "local", Name = "Alfa", Path = "a.jpg", ModifiedAt = s_now },
            ],
        });
        db.SavedPlaces.Add(new SavedPlace { Name = "Casa", Location = new Point(-9.0, 38.0) { SRID = 4326 }, RadiusM = 100, IsHome = true });
        await db.SaveChangesAsync();
        var embedding = new Vector(new float[128]);
        db.Faces.AddRange(
            new Face { AssetId = "a4", Box = [0.7, 0.1, 0.1, 0.1], Score = 0.9, Embedding = embedding, Person = bruno, Assignment = FaceAssignments.Manual },
            new Face { AssetId = "a4", Box = [0.1, 0.1, 0.1, 0.1], Score = 0.8, Embedding = embedding, Person = bruno, Assignment = FaceAssignments.Auto },
            new Face { AssetId = "a4", Box = [0.4, 0.1, 0.1, 0.1], Score = 0.7, Embedding = embedding, Person = carla, Assignment = FaceAssignments.Excluded },
            new Face { AssetId = "a4", Box = [0.9, 0.1, 0.1, 0.1], Score = 0.6, Embedding = embedding, Person = carla, Assignment = FaceAssignments.NotFace });
        await db.SaveChangesAsync();
        var request = await RequestAsync(db, includeFaces: true);

        await Generator(db).RunPendingAsync(CancellationToken.None);

        var (catalog, _, _) = await ReadZipAsync(db, request);
        using var json = JsonDocument.Parse(catalog);
        var asset = json.RootElement.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("id").GetString() == "a4");
        // Bruno once (two faces that count), Carla not at all (excluded, not a face).
        Assert.Equal(["Bruno"], asset.GetProperty("people").EnumerateArray().Select(p => p.GetString()));
        // The faces by position from the left, all four, with their assignment.
        Assert.Equal([0.1, 0.4, 0.7, 0.9], asset.GetProperty("faces").EnumerateArray().Select(f => f.GetProperty("box")[0].GetDouble()));
        Assert.Equal(["auto", "excluded", "manual", "not_face"], asset.GetProperty("faces").EnumerateArray().Select(f => f.GetProperty("assignment").GetString()));
        Assert.Equal(["Alfa/a.jpg", "Alfa/z.jpg", "Zeta/b.jpg"], asset.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("name").GetString() + "/" + s.GetProperty("path").GetString()));
        Assert.True(asset.GetProperty("hidden").GetBoolean());
        Assert.False(json.RootElement.GetProperty("assets")[0].GetProperty("hidden").GetBoolean());
        var places = json.RootElement.GetProperty("savedPlaces").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("home").GetBoolean());
        Assert.Equal((true, false), (places["Casa"], places["Casa da praia"]));
    }

    [Fact]
    public async Task The_file_name_of_an_original_is_the_last_part_of_its_path_with_tabs_and_line_breaks_made_spaces_and_the_asset_id_when_nothing_is_left()
    {
        await using var db = await SeedAsync();
        foreach (var (id, path) in new[] { ("a4", "dir/with\ttab.jpg"), ("a5", "x/line\nbreak.jpg"), ("a6", "folder/"), ("a7", "C:\\Fotos\\win.jpg"), ("a8", " \t ") })
        {
            db.Assets.Add(new Asset
            {
                Id = id,
                Kind = "photo",
                ContentType = "image/jpeg",
                SizeBytes = 10,
                ImportedAt = s_now,
                UpdatedAt = s_now,
                Sources = [new AssetSource { AssetId = id, Type = "local", Name = "HD", Path = path, ModifiedAt = s_now }],
            });
        }

        await db.SaveChangesAsync();
        var request = await RequestAsync(db, includeFaces: false);

        await Generator(db).RunPendingAsync(CancellationToken.None);

        var (_, originals, _) = await ReadZipAsync(db, request);
        var names = originals.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).Select(l => l.Split('\t')).ToDictionary(p => p[0], p => p[1]);
        Assert.All(originals.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')), line => Assert.Equal(3, line.Split('\t').Length));
        Assert.Equal("with tab.jpg", names["a4"]);
        Assert.Equal("line break.jpg", names["a5"]);
        Assert.Equal("a6", names["a6"]);
        Assert.Equal("win.jpg", names["a7"]);
        Assert.Equal("a8", names["a8"]);
    }

    [Fact]
    public async Task The_readme_of_an_export_with_face_data_says_so_and_warns_that_it_is_biometric()
    {
        await using var db = await SeedAsync();
        var request = await RequestAsync(db, includeFaces: true);
        await Generator(db).RunPendingAsync(CancellationToken.None);

        var (_, _, readme) = await ReadZipAsync(db, request);

        Assert.Contains("Face data is included", readme, StringComparison.Ordinal);
        Assert.Contains("biometric data; keep this zip private", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Face data (face boxes and embeddings) is not included", readme, StringComparison.Ordinal);
        Assert.Contains("Os dados de rostos estão incluídos", readme, StringComparison.Ordinal);
    }

    private ExportGenerator Generator(CatalogDbContext db) =>
        new(db, new AzureBlobStore(fixture.Blobs, TimeProvider.System, ExportGenerator.OriginalLinkLifetime), TimeProvider.System, NullLogger.Instance);

    private static async Task<ExportRequest> RequestAsync(CatalogDbContext db, bool includeFaces)
    {
        var request = new ExportRequest { RequestedAt = s_now, RequestedBy = "ana@example.com", IncludeFaceData = includeFaces, Status = ExportStatuses.Requested };
        db.ExportRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private async Task<(string Catalog, string Originals, string Readme)> ReadZipAsync(CatalogDbContext db, ExportRequest request)
    {
        var name = (await db.ExportRequests.AsNoTracking().SingleAsync(e => e.Id == request.Id)).BlobName!;
        using var content = (await fixture.Store.ReadAsync("exports", name))!.Content.ToStream();
        using var zip = new ZipArchive(content);
        Assert.Equal(["README.txt", "catalog.json", "originals.txt"], zip.Entries.Select(e => e.FullName));
        string Text(string entry) => new StreamReader(zip.GetEntry(entry)!.Open()).ReadToEnd();
        return (Text("catalog.json"), Text("originals.txt"), Text("README.txt"));
    }

    private async Task<CatalogDbContext> SeedAsync()
    {
        foreach (var container in new[] { "exports", "originals" })
        {
            await fixture.Blobs.GetBlobContainerClient(container).CreateIfNotExistsAsync();
        }

        var db = await fixture.CreateMigratedDatabaseAsync();
        db.Assets.Add(new Asset
        {
            Id = "a1",
            Kind = "photo",
            ContentType = "image/jpeg",
            SizeBytes = 2000,
            CapturedAt = new DateTime(2019, 7, 14, 10, 30, 0),
            Location = new Point(-9.1, 38.7) { SRID = 4326 },
            CameraMake = "Apple",
            CameraModel = "iPhone",
            Width = 4000,
            Height = 3000,
            PlaceCity = "Lisbon",
            PlaceCountry = "PT",
            ImportedAt = s_now,
            UpdatedAt = s_now,
            Sources =
            [
                new AssetSource { AssetId = "a1", Type = "local", Name = "HD da Ana", Path = "2019/IMG_1.jpg", ModifiedAt = s_now },
                new AssetSource { AssetId = "a1", Type = "local", Name = "Celular", Path = "DCIM/x/IMG_1.jpg", ModifiedAt = s_now },
            ],
        });
        db.Assets.Add(new Asset
        {
            Id = "a2",
            Kind = "video",
            ContentType = "video/quicktime",
            SizeBytes = 9000,
            DurationSeconds = 12.5,
            ImportedAt = s_now,
            UpdatedAt = s_now,
            Sources = [new AssetSource { AssetId = "a2", Type = "local", Name = "HD da Ana", Path = "videos/a2.mov", ModifiedAt = s_now }],
        });
        db.Assets.Add(new Asset
        {
            Id = "a3",
            Kind = "photo",
            ContentType = "image/jpeg",
            SizeBytes = 3000,
            OriginalStored = false,
            ImportedAt = s_now,
            UpdatedAt = s_now,
            Sources = [new AssetSource { AssetId = "a3", Type = "gdrive", Name = "Família", Path = "Fotos/c.jpg", ModifiedAt = s_now, Url = "https://drive.example.com/file/3" }],
        });
        var ana = new Person { Name = "Ana", UserEmail = "ana@example.com", FaceCount = 1 };
        db.People.Add(ana);
        var embedding = new float[128];
        embedding[3] = 1f;
        db.Faces.Add(new Face { AssetId = "a1", Box = [0.1, 0.2, 0.3, 0.4], Score = 0.9, Embedding = new Vector(embedding), Person = ana, Assignment = FaceAssignments.Auto });
        db.SavedPlaces.Add(new SavedPlace { Name = "Casa da praia", Location = new Point(-9.4, 38.9) { SRID = 4326 }, RadiusM = 300, IsHome = false });
        await db.SaveChangesAsync();
        var occasion = new Occasion { StartsAt = new DateTime(2019, 7, 14), EndsAt = new DateTime(2019, 7, 15), AssetCount = 2, Type = "trip" };
        db.Occasions.Add(occasion);
        await db.SaveChangesAsync();
        db.OccasionAssets.AddRange(new OccasionAsset { EventId = occasion.Id, AssetId = "a1" }, new OccasionAsset { EventId = occasion.Id, AssetId = "a2" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await fixture.Store.WriteAsync("originals", "a1", BinaryData.FromBytes([1]), "image/jpeg", BlobWriteCondition.Always);
        await fixture.Store.WriteAsync("originals", "a2", BinaryData.FromBytes([2]), "video/quicktime", BlobWriteCondition.Always);
        return db;
    }

    private sealed class ThrowingStore(IBlobStore inner, Exception exception) : DelegatingBlobStore(inner)
    {
        public override IStagedUpload BeginUpload(string container, string name) => throw exception;
    }

    private sealed class FailOnceStore(IBlobStore inner) : DelegatingBlobStore(inner)
    {
        private int _failed;

        public override IStagedUpload BeginUpload(string container, string name) =>
            Interlocked.Exchange(ref _failed, 1) == 0 ? throw new IOException("storage is down") : base.BeginUpload(container, name);
    }
}
