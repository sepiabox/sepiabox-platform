using System.IO.Compression;
using System.Text;
using Sepiabox.Search.Clip;

namespace Sepiabox.Search.Tests;

/// <summary>The BPE algorithm on a tiny merges file written here; the real ids are in <see cref="ClipTokenizerReferenceTests"/>.</summary>
public sealed class ClipTokenizerTests
{
    // 256 byte symbols, the same with </w>, then one id per merge, then start and end.
    private const int FirstMerge = 512;

    private static readonly ClipTokenizer s_tokenizer = Tokenizer("c a", "ca t</w>", "d o", "do g</w>");

    private static int Start => FirstMerge + 4;

    private static int End => FirstMerge + 5;

    [Fact]
    public void Merges_a_word_into_one_symbol_when_its_pairs_are_in_the_merges()
    {
        Assert.Equal([Start, FirstMerge + 1, End], s_tokenizer.Encode("cat"));
        Assert.Equal(FirstMerge + 6, s_tokenizer.VocabularySize);
    }

    [Fact]
    public void Ends_each_word_with_the_end_of_word_suffix()
    {
        // 'a' is the 65th printable byte: "a" is 64 and "a</w>" 256 + 64, as in CLIP's real vocabulary.
        Assert.Equal([Start, 320, FirstMerge + 1, FirstMerge + 3, End], s_tokenizer.Encode("a cat dog"));
    }

    [Fact]
    public void Lower_cases_collapses_white_space_and_unescapes_html_before_splitting()
    {
        // "&" is byte 38, the 6th printable one: "&</w>" is 256 + 5.
        Assert.Equal([Start, FirstMerge + 1, 261, End], s_tokenizer.Encode("  CAT&amp;amp;  "));
    }

    [Fact]
    public void Splits_letters_digits_and_punctuation_as_clip_does()
    {
        var ids = s_tokenizer.Encode("dog42!");

        // "dog", "4", "2", "!": digits one by one.
        Assert.Equal(6, ids.Count);
        Assert.Equal(FirstMerge + 3, ids[1]);
    }

    [Fact]
    public void Encodes_letters_outside_ascii_byte_by_byte()
    {
        var ids = s_tokenizer.Encode("pão");

        Assert.Equal(Start, ids[0]);
        Assert.Equal(End, ids[^1]);
        // p, the two bytes of ã, and o</w>: no merge applies.
        Assert.Equal(6, ids.Count);
    }

    [Fact]
    public void Pads_with_zeros_to_77_ids_for_the_model()
    {
        var ids = s_tokenizer.EncodeForModel("cat", out var length);

        Assert.Equal(ClipTokenizer.ContextLength, ids.Length);
        Assert.Equal(3, length);
        Assert.Equal([Start, FirstMerge + 1, End], ids.Take(3).Select(i => (int)i));
        Assert.All(ids.Skip(3), id => Assert.Equal(0, id));
    }

    [Fact]
    public void Cuts_a_long_text_to_77_ids_keeping_the_end_token_last()
    {
        var ids = s_tokenizer.EncodeForModel(string.Join(' ', Enumerable.Repeat("cat", 200)), out var length);

        Assert.Equal(ClipTokenizer.ContextLength, length);
        Assert.Equal(Start, ids[0]);
        Assert.Equal(End, ids[^1]);
        Assert.Equal(FirstMerge + 1, ids[^2]);
    }

    [Fact]
    public void Refuses_a_merges_file_with_a_malformed_line()
    {
        Assert.Throws<InvalidDataException>(() => Tokenizer("c a", "oops"));
    }

    private static ClipTokenizer Tokenizer(params string[] merges)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes("\"bpe_simple_vocab_16e6.txt#version: 0.2\n" + string.Join('\n', merges) + "\n"));
        }

        buffer.Position = 0;
        return ClipTokenizer.Load(buffer);
    }
}
