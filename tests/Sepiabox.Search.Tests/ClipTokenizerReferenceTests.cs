using Sepiabox.Search.Clip;

namespace Sepiabox.Search.Tests;

/// <summary>CLIP's real tokenizer, built from the pinned merges file (S-016): the ids OpenAI's and Hugging Face's give.</summary>
[Trait("Category", "Integration")]
public sealed class ClipTokenizerReferenceTests
{
    [ClipBpeFact]
    public void Tokenizes_a_photo_of_a_cat_into_the_reference_ids_then_padding()
    {
        var tokenizer = Load();

        var ids = tokenizer.EncodeForModel("a photo of a cat", out var length);

        Assert.Equal([49406, 320, 1125, 539, 320, 2368, 49407], ids.Take(length).Select(i => (int)i));
        Assert.All(ids.Skip(length), id => Assert.Equal(0, id));
        Assert.Equal(49408, tokenizer.VocabularySize);
    }

    [ClipBpeFact]
    public void Tokenizes_case_and_punctuation_like_clip()
    {
        var tokenizer = Load();

        // "A Photo of a CAT!" is "a photo of a cat !" to CLIP; "!" is a word of its own.
        Assert.Equal([49406, 320, 1125, 539, 320, 2368, 256, 49407], tokenizer.Encode("A Photo  of a CAT!"));
    }

    private static ClipTokenizer Load() =>
        ClipTextEncoder.TryLoadTokenizer(TestModels.Folder, out var warning) ?? throw new InvalidOperationException(warning);
}
