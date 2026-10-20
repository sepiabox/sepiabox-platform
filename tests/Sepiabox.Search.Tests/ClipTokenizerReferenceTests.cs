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

    [ClipBpeFact]
    public void Straightens_curly_quotes_as_ftfy_does_for_the_reference()
    {
        // OpenAI's simple_tokenizer.py (through ftfy) gives 847 713 257 2583 257 for "don’t “snow”".
        Assert.Equal([49406, 847, 713, 257, 2583, 257, 49407], Load().Encode("don\u2019t \u201Csnow\u201D"));
    }

    [ClipBpeFact]
    public void Reads_full_width_letters_ligatures_and_decomposed_accents_as_their_plain_forms()
    {
        var tokenizer = Load();

        // ftfy narrows full-width forms, undoes ligatures and composes accents; Python gives 2117 for "Ｂｅａｃｈ" and
        // 15304 45911 for "café São" either way.
        Assert.Equal([49406, 2117, 49407], tokenizer.Encode("\uFF22\uFF45\uFF41\uFF43\uFF48"));
        Assert.Equal(tokenizer.Encode("beach"), tokenizer.Encode("\uFF22\uFF45\uFF41\uFF43\uFF48"));
        Assert.Equal(tokenizer.Encode("fine"), tokenizer.Encode("\uFB01ne"));
        Assert.Equal([49406, 15304, 45911, 49407], tokenizer.Encode("cafe\u0301 Sa\u0303o"));
        Assert.Equal(tokenizer.Encode("caf\u00E9 S\u00E3o"), tokenizer.Encode("cafe\u0301 Sa\u0303o"));
    }

    private static ClipTokenizer Load() =>
        ClipTextEncoder.TryLoadTokenizer(TestModels.Folder, out var warning) ?? throw new InvalidOperationException(warning);
}
