using ImageMagick;
using Sepiabox.Pipeline.Embeddings;

namespace Sepiabox.Pipeline.Tests;

[Trait("Category", "Integration")]
public sealed class ClipImageEmbedderTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [ClipFact]
    public void An_image_has_a_512_value_embedding_of_length_one()
    {
        using var embedder = CreateEmbedder();

        var embedding = embedder.Embed(WriteScene("scene.webp", 640, 480));

        Assert.Equal(512, embedding.Length);
        var norm = Math.Sqrt(embedding.Sum(v => (double)v * v));
        Assert.InRange(norm, 0.999, 1.001);
    }

    [ClipFact]
    public void The_same_image_twice_gives_the_same_embedding()
    {
        using var embedder = CreateEmbedder();
        var path = WriteScene("scene.webp", 640, 480);

        var cosine = EmbeddingCodec.Cosine(embedder.Embed(path), embedder.Embed(path));

        Assert.True(cosine > 0.999, $"cosine was {cosine}");
    }

    [ClipFact]
    public void An_image_and_its_resized_version_are_close()
    {
        using var embedder = CreateEmbedder();
        var large = WriteScene("large.webp", 1280, 960);
        string small;
        using (var image = new MagickImage(large))
        {
            image.Resize(320, 240);
            small = Path.Combine(_folder.Path, "small.webp");
            image.Write(small);
        }

        var cosine = EmbeddingCodec.Cosine(embedder.Embed(large), embedder.Embed(small));

        Assert.True(cosine > 0.9, $"cosine was {cosine}");
    }

    [ClipFact]
    public void A_resized_copy_is_closer_than_a_different_picture()
    {
        using var embedder = CreateEmbedder();
        var large = WriteScene("large.webp", 1280, 960);
        string small, flat;
        using (var image = new MagickImage(large))
        {
            image.Resize(320, 240);
            small = Path.Combine(_folder.Path, "small.webp");
            image.Write(small);
        }

        using (var image = new MagickImage(MagickColors.Black, 640, 480))
        {
            flat = Path.Combine(_folder.Path, "flat.webp");
            image.Write(flat);
        }

        var scene = embedder.Embed(large);
        var copy = EmbeddingCodec.Cosine(scene, embedder.Embed(small));
        var different = EmbeddingCodec.Cosine(scene, embedder.Embed(flat));

        Assert.True(copy > different, $"copy {copy}, different picture {different}");
    }

    // The reference is Hugging Face transformers' CLIPProcessor + CLIPModel.get_image_features on
    // openai/clip-vit-base-patch32, computed once for these committed images (S-015) and normalized.
    [ClipTheory]
    [InlineData("clip-landscape")]
    [InlineData("clip-portrait")]
    public void Matches_the_reference_implementation_on_a_golden_image(string name)
    {
        using var embedder = CreateEmbedder();
        var reference = EmbeddingCodec.Decode(File.ReadAllText(Path.Combine("TestData", $"{name}.image_embeds.txt")).Trim());

        var cosine = EmbeddingCodec.Cosine(embedder.Embed(Path.Combine("TestData", $"{name}.png")), reference);

        Assert.True(cosine > 0.999, $"cosine with the reference was {cosine}");
    }

    private static ClipImageEmbedder CreateEmbedder() =>
        ClipImageEmbedder.TryCreate(TestModels.Folder, out var warning) ?? throw new InvalidOperationException(warning);

    // A deterministic picture with structure (sky, sun, ground), so the model sees more than a flat colour.
    private string WriteScene(string name, uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.SkyBlue, width, height);
        image.Draw(new ImageMagick.Drawing.Drawables()
            .FillColor(MagickColors.ForestGreen).Rectangle(0, height * 2 / 3, width, height)
            .FillColor(MagickColors.Gold).Circle(width * 3 / 4, height / 4, (width * 3 / 4) + (width / 10), height / 4));
        var path = Path.Combine(_folder.Path, name);
        image.Write(path);
        return path;
    }
}
