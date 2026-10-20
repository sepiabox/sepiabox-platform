using Sepiabox.Search.Clip;

namespace Sepiabox.Search.Tests;

public sealed class ClipTextEncoderTests
{
    [Fact]
    public void Says_why_there_is_no_encoder_when_the_files_are_missing()
    {
        using var empty = new TempFolder();

        Assert.Null(ClipTextEncoder.TryCreate(empty.Path, out var warning));

        Assert.NotNull(warning);
        Assert.Contains("search by description is off", warning, StringComparison.Ordinal);
        Assert.False(ClipTextEncoder.FilesPresent(empty.Path));
    }

    [Fact]
    public void Normalizes_to_length_one_and_refuses_a_zero_vector()
    {
        float[] vector = [3, 4];

        ClipTextEncoder.Normalize(vector);

        Assert.Equal([0.6f, 0.8f], vector);
        Assert.Throws<InvalidOperationException>(() => ClipTextEncoder.Normalize([0, 0]));
        Assert.Throws<InvalidOperationException>(() => ClipTextEncoder.Normalize([float.NaN, 1]));
    }
}

/// <summary>Text inference with the real CLIP text model (S-016).</summary>
[Trait("Category", "Integration")]
public sealed class ClipTextEncoderModelTests
{
    [ClipTextFact]
    public void Embeds_a_text_into_512_values_with_norm_1()
    {
        using var encoder = ClipTextEncoder.TryCreate(TestModels.Folder, out var warning) ?? throw new InvalidOperationException(warning);

        var embedding = encoder.Encode("a photo of a cat");

        Assert.Equal(ClipTextEncoder.Dimensions, embedding.Length);
        Assert.Equal(1, Math.Sqrt(embedding.Sum(v => (double)v * v)), 4);
    }

    [ClipTextFact]
    public void Puts_related_descriptions_closer_together_than_unrelated_ones()
    {
        using var encoder = ClipTextEncoder.TryCreate(TestModels.Folder, out var warning) ?? throw new InvalidOperationException(warning);

        var cat = encoder.Encode("a photo of a cat");
        var kitten = encoder.Encode("a photo of a kitten");
        var spreadsheet = encoder.Encode("a spreadsheet of tax figures");

        Assert.True(Dot(cat, kitten) > Dot(cat, spreadsheet));
    }

    private static double Dot(float[] a, float[] b) => a.Zip(b, (x, y) => (double)x * y).Sum();
}
