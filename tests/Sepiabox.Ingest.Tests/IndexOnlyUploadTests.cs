using System.Diagnostics;
using Azure.Storage.Blobs;
using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Uploading;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Testcontainers.Azurite;

namespace Sepiabox.Ingest.Tests;

/// <summary>upload follows the work folder's mode (S-063, D-088): an index-only work folder never sends an original.</summary>
[Trait("Category", "Integration")]
public sealed class IndexOnlyUploadTests : IAsyncLifetime, IDisposable
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        .WithInMemoryPersistence()
        // The storage SDK speaks a newer API version than Azurite announces; Aspire's emulator does the same.
        .WithCommand("--skipApiVersionCheck")
        .Build();

    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();
    private readonly TempFolder _otherDrive = new();
    private readonly TempFolder _otherWork = new();

    private BlobServiceClient Service => new(_azurite.GetConnectionString());

    public Task InitializeAsync() => _azurite.StartAsync();

    public async Task DisposeAsync() => await _azurite.DisposeAsync();

    public void Dispose()
    {
        foreach (var folder in new[] { _drive, _work, _otherDrive, _otherWork }.Where(f => Directory.Exists(f.Path)))
        {
            folder.Dispose();
        }
    }

    [Fact]
    public async Task Uploads_derivatives_and_manifests_and_no_original_when_the_source_folder_is_gone()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Sized(3000, 2000));
        _drive.Write("2019/serra.jpg", TestPhotos.Sized(1000, 750));
        var manifests = ProcessFolder(_drive, _work, "HD da Ana", indexOnly: true);
        Directory.Delete(_drive.Path, recursive: true);

        var result = await Upload(_work);

        Assert.Empty(result.Problems);
        Assert.Empty(result.Warnings);
        Assert.True(result.IndexOnly);
        Assert.Equal(2, result.Assets);
        Assert.Equal(2, result.OriginalsLeftAtSource);
        // Four derivatives and two manifests, no originals.
        Assert.Equal(6, result.Uploaded);
        Assert.Empty(await BlobNames(BlobLayout.OriginalsContainer));
        var ids = manifests.Select(m => m.Id).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            ids.SelectMany(id => new[] { $"{id}/preview-2048.webp", $"{id}/thumb-256.webp" }),
            await BlobNames(BlobLayout.DerivativesContainer));
        Assert.Equal(ids.Select(id => id + ".json"), await BlobNames(BlobLayout.ManifestsContainer));
        foreach (var id in ids)
        {
            Assert.False((await StoredManifest(id)).IsOriginalStored);
        }

        Assert.DoesNotContain(ReadUploads(_work), r => r.Container == BlobLayout.OriginalsContainer);
    }

    [Fact]
    public async Task Uploading_again_leaves_the_originals_at_their_source_again_and_sends_nothing()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        ProcessFolder(_drive, _work, "HD da Ana", indexOnly: true);
        await Upload(_work);

        var second = await Upload(_work);

        Assert.Equal((0, 0), (second.Uploaded, second.Problems.Count));
        Assert.Equal(1, second.OriginalsLeftAtSource);
        Assert.Empty(await BlobNames(BlobLayout.OriginalsContainer));
    }

    [Fact]
    public async Task A_full_work_folder_still_uploads_its_original_and_leaves_none_at_the_source()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var manifest = Assert.Single(ProcessFolder(_drive, _work, "HD da Ana", indexOnly: false));

        var result = await Upload(_work);

        Assert.False(result.IndexOnly);
        Assert.Equal(0, result.OriginalsLeftAtSource);
        Assert.Equal([manifest.Id], await BlobNames(BlobLayout.OriginalsContainer));
        Assert.True((await StoredManifest(manifest.Id)).IsOriginalStored);
    }

    [Fact]
    public async Task An_asset_uploaded_in_full_mode_stays_stored_when_the_same_bytes_are_uploaded_index_only_after_it()
    {
        var photo = TestPhotos.Porto("red");
        _drive.Write("2019/praia.jpg", photo);
        _otherDrive.Write("Google Photos/IMG_0001.jpg", photo);
        var id = Assert.Single(ProcessFolder(_drive, _work, "HD da Ana", indexOnly: false)).Id;
        Assert.Equal(id, Assert.Single(ProcessFolder(_otherDrive, _otherWork, "Takeout 2026", indexOnly: true)).Id);

        await Upload(_work);
        Directory.Delete(_otherDrive.Path, recursive: true);
        var second = await Upload(_otherWork);

        Assert.Empty(second.Problems);
        var stored = await StoredManifest(id);
        Assert.True(stored.IsOriginalStored);
        Assert.Equal(["HD da Ana", "Takeout 2026"], stored.Sources.Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.Equal([id], await BlobNames(BlobLayout.OriginalsContainer));
        // Both previews, side by side, and the thumbnail they share.
        Assert.Equal(
            [$"{id}/preview-1280.webp", $"{id}/preview-2048.webp", $"{id}/thumb-256.webp"],
            await BlobNames(BlobLayout.DerivativesContainer));
    }

    [Fact]
    public async Task An_asset_uploaded_index_only_becomes_stored_when_the_same_bytes_are_uploaded_in_full_mode_after_it()
    {
        var photo = TestPhotos.Porto("red");
        _drive.Write("2019/praia.jpg", photo);
        _otherDrive.Write("Google Photos/IMG_0001.jpg", photo);
        var id = Assert.Single(ProcessFolder(_otherDrive, _otherWork, "Takeout 2026", indexOnly: true)).Id;
        ProcessFolder(_drive, _work, "HD da Ana", indexOnly: false);

        await Upload(_otherWork);
        Assert.False((await StoredManifest(id)).IsOriginalStored);
        Assert.Empty(await BlobNames(BlobLayout.OriginalsContainer));
        var second = await Upload(_work);

        Assert.Empty(second.Problems);
        var stored = await StoredManifest(id);
        Assert.True(stored.IsOriginalStored);
        Assert.Equal(["HD da Ana", "Takeout 2026"], stored.Sources.Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.Equal([id], await BlobNames(BlobLayout.OriginalsContainer));
    }

    [Fact]
    public async Task A_source_url_in_the_work_folder_manifest_reaches_the_stored_manifest()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        var manifest = Assert.Single(ProcessFolder(_drive, _work, "Takeout 2026", indexOnly: true));
        var path = Path.Combine(_work.Path, "manifests", manifest.Id + ".json");
        // The export readers fill the url in (S-046, S-048); here it is only carried through.
        ManifestJson.WriteFile(path, manifest with { Sources = [.. manifest.Sources.Select(s => s with { Url = "https://photos.example.com/p/1" })] });

        var result = await Upload(_work);

        Assert.Empty(result.Problems);
        Assert.Equal("https://photos.example.com/p/1", Assert.Single((await StoredManifest(manifest.Id)).Sources).Url);
    }

    [Fact]
    public async Task The_program_summary_counts_the_originals_left_at_their_source_only_for_an_index_only_work_folder()
    {
        _drive.Write("a.jpg", TestPhotos.Porto("red"));
        _drive.Write("b.jpg", TestPhotos.Porto("green"));
        _otherDrive.Write("c.jpg", TestPhotos.Porto("blue"));
        ProcessFolder(_drive, _work, "HD da Ana", indexOnly: true);
        ProcessFolder(_otherDrive, _otherWork, "HD do Jonathas", indexOnly: false);

        var (indexOnlyCode, indexOnlyOutput) = RunUpload(_work);
        var (fullCode, fullOutput) = RunUpload(_otherWork);

        Assert.Equal(0, indexOnlyCode);
        Assert.Contains("originals left at their source 2,", indexOnlyOutput, StringComparison.Ordinal);
        Assert.Equal(0, fullCode);
        Assert.DoesNotContain("left at their source", fullOutput, StringComparison.Ordinal);
    }

    private (int ExitCode, string Output) RunUpload(TempFolder work)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.Environment[UploadCommand.ConnectionVariable] = _azurite.GetConnectionString();
        foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "sepiabox-ingest.dll"), "upload", work.Path, "--destination", "local" })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }

    private static List<Manifest> ProcessFolder(TempFolder drive, TempFolder work, string sourceName, bool indexOnly)
    {
        // No FFmpeg in these tests: photos get their previews.
        ProcessCommand.Run(new ProcessOptions(drive.Path, work.Path, sourceName, FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None, IndexOnly: indexOnly));
        return [.. Directory.GetFiles(Path.Combine(work.Path, "manifests"), "*.json")
            .Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];
    }

    private Task<UploadResult> Upload(TempFolder work) =>
        UploadCommand.RunAsync(new UploadOptions(work.Path, UploadDestination.Local), Service);

    private static List<UploadRecord> ReadUploads(TempFolder work)
    {
        using var state = new UploadState(Path.Combine(work.Path, "state.db"));
        return [.. state.All()];
    }

    private async Task<Manifest> StoredManifest(string id) =>
        ManifestJson.Deserialize((await Service.GetBlobContainerClient(BlobLayout.ManifestsContainer)
            .GetBlobClient(BlobLayout.ManifestBlobName(id)).DownloadContentAsync()).Value.Content.ToString());

    private async Task<List<string>> BlobNames(string container)
    {
        var client = Service.GetBlobContainerClient(container);
        if (!await client.ExistsAsync())
        {
            return [];
        }

        var names = new List<string>();
        await foreach (var blob in client.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }

        return [.. names.Order(StringComparer.Ordinal)];
    }
}
