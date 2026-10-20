using ImageMagick;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Tests;

[Trait("Category", "Integration")]
public sealed class EmbeddingProcessingTests
{
    [ClipFact]
    public void Process_stores_the_clip_embedding_of_each_photo_in_its_manifest()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("2019/praia.jpg", Scene());

        var result = ProcessCommand.Run(Options(drive, work, TestModels.Folder));

        Assert.Equal(1, result.Embedded);
        var manifest = Assert.Single(ReadManifests(work));
        var embedding = EmbeddingCodec.Decode(manifest.Embeddings[ClipImageEmbedder.ModelKey].GetValue<string>());
        Assert.Equal(512, embedding.Length);
        Assert.InRange(Math.Sqrt(embedding.Sum(v => (double)v * v)), 0.999, 1.001);
    }

    [ClipFact]
    public void A_later_run_with_the_model_adds_embeddings_from_the_work_folder()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        using var noModels = new TempFolder();
        drive.Write("2019/praia.jpg", Scene());
        ProcessCommand.Run(Options(drive, work, noModels.Path));

        var result = ProcessCommand.Run(Options(drive, work, TestModels.Folder));

        Assert.Equal((0, 1, 1), (result.Processed, result.Skipped, result.Embedded));
        Assert.True(Assert.Single(ReadManifests(work)).Embeddings.ContainsKey(ClipImageEmbedder.ModelKey));

        var third = ProcessCommand.Run(Options(drive, work, TestModels.Folder));
        Assert.Equal(0, third.Embedded);
    }

    private static ProcessOptions Options(TempFolder drive, TempFolder work, string models) =>
        new(drive.Path, work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: models);

    private static List<Manifest> ReadManifests(TempFolder work) =>
        [.. Directory.GetFiles(Path.Combine(work.Path, "manifests"), "*.json").Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];

    private static byte[] Scene()
    {
        using var image = new MagickImage(MagickColors.SkyBlue, 800, 600);
        image.Draw(new ImageMagick.Drawing.Drawables()
            .FillColor(MagickColors.SandyBrown).Rectangle(0, 400, 800, 600)
            .FillColor(MagickColors.Gold).Circle(600, 150, 680, 150));
        return image.ToByteArray(MagickFormat.Jpeg);
    }
}
