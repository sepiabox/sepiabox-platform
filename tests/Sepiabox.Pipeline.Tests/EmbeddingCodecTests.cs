using Sepiabox.Pipeline.Embeddings;

namespace Sepiabox.Pipeline.Tests;

public sealed class EmbeddingCodecTests
{
    [Fact]
    public void Encodes_float32_little_endian_as_base64()
    {
        // 1.0f is 0x3F800000: bytes 00 00 80 3F in little-endian order.
        Assert.Equal("AACAPw==", EmbeddingCodec.Encode([1f]));
    }

    [Fact]
    public void Decoding_returns_what_was_encoded()
    {
        float[] values = [0.5f, -1.25f, 3.1415927f, float.Epsilon, 0f];

        Assert.Equal(values, EmbeddingCodec.Decode(EmbeddingCodec.Encode(values)));
    }

    [Fact]
    public void A_clip_embedding_is_2732_base64_characters()
    {
        Assert.Equal(2732, EmbeddingCodec.Encode(new float[ClipImageEmbedder.Dimensions]).Length);
    }

    [Theory]
    [InlineData("AAA=")]
    [InlineData("not base64!")]
    public void Decoding_rejects_text_that_is_not_whole_float32_values(string text)
    {
        Assert.Throws<FormatException>(() => EmbeddingCodec.Decode(text));
    }

    [Fact]
    public void Normalize_scales_to_length_one()
    {
        float[] values = [3f, 4f];

        EmbeddingCodec.Normalize(values);

        Assert.Equal(0.6f, values[0], 6);
        Assert.Equal(0.8f, values[1], 6);
    }

    [Fact]
    public void Normalize_refuses_a_zero_vector()
    {
        Assert.Throws<EmbeddingException>(() => EmbeddingCodec.Normalize(new float[3]));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Normalize_refuses_a_vector_with_a_value_that_is_not_finite(float value)
    {
        float[] values = [1f, value, 2f];

        var failure = Assert.Throws<EmbeddingException>(() => EmbeddingCodec.Normalize(values));

        Assert.Contains("not finite", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cosine_of_a_vector_with_itself_is_one_and_with_its_opposite_minus_one()
    {
        float[] a = [1f, 2f, 3f];
        float[] opposite = [-1f, -2f, -3f];

        Assert.Equal(1, EmbeddingCodec.Cosine(a, a), 6);
        Assert.Equal(-1, EmbeddingCodec.Cosine(a, opposite), 6);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void Try_normalize_refuses_without_throwing_and_leaves_the_vector_unchanged(float value)
    {
        float[] values = [value, value];

        Assert.False(EmbeddingCodec.TryNormalize(values));
        Assert.Equal([value, value], values);
    }

    [Fact]
    public void Try_normalize_scales_a_usable_vector_to_length_one()
    {
        float[] values = [3f, 4f];

        Assert.True(EmbeddingCodec.TryNormalize(values));
        Assert.Equal([0.6f, 0.8f], values);
    }
}
