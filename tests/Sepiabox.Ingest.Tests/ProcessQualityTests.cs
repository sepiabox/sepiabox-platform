using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Tests;

/// <summary>A photo's sharpness and hash in its manifest (S-027, D-098), with no model and nothing read from the drive twice.</summary>
public sealed class ProcessQualityTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void A_new_photo_gets_its_quality_in_the_manifest_without_a_model()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());

        var result = Run();

        Assert.Equal(1, result.Processed);
        Assert.Empty(result.Problems);
        var quality = Manifests().Single().Quality;
        Assert.NotNull(quality);
        Assert.True(QualityAnalyzer.IsHash(quality.DHash));
    }

    [Fact]
    public void An_unchanged_photo_processed_before_the_measure_existed_gets_it_from_its_preview_and_is_then_left_alone()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        Run();
        var path = Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Single();
        ManifestJson.WriteFile(path, ManifestJson.Deserialize(File.ReadAllText(path)) with { Quality = null });

        var second = Run();
        var written = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, written.AddHours(-1));
        var third = Run();

        Assert.Equal(1, second.Skipped);
        Assert.NotNull(ManifestJson.Deserialize(File.ReadAllText(path)).Quality);
        Assert.Equal(1, third.Skipped);
        Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_preview_that_cannot_be_measured_is_a_warning_and_the_photo_stays_done()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        Run();
        var path = Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Single();
        var manifest = ManifestJson.Deserialize(File.ReadAllText(path));
        ManifestJson.WriteFile(path, manifest with { Quality = null });
        foreach (var derivative in manifest.Derivatives)
        {
            File.WriteAllBytes(Path.Combine(_work.Path, derivative.Path), [1, 2, 3]);
        }

        var result = Run();

        Assert.Empty(result.Problems);
        Assert.Contains("sharpness", Assert.Single(result.EmbeddingProblems).Detail, StringComparison.Ordinal);
        Assert.Null(ManifestJson.Deserialize(File.ReadAllText(path)).Quality);
    }

    private ProcessResult Run() =>
        ProcessCommand.Run(
            new ProcessOptions(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None),
            null,
            "no model in this test",
            null);

    private List<Manifest> Manifests() =>
        [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Select(f => ManifestJson.Deserialize(File.ReadAllText(f)))];
}
