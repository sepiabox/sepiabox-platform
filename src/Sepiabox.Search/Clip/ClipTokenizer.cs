using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Sepiabox.Search.Clip;

/// <summary>
/// CLIP's byte-level BPE tokenizer, as OpenAI's <c>SimpleTokenizer</c> builds it from
/// <c>bpe_simple_vocab_16e6.txt.gz</c> (S-016, D-070): lower case, CLIP's split pattern, <c>&lt;/w&gt;</c> at the end of
/// each word, start 49406, end 49407, 77 ids padded with 0. Hugging Face's <c>CLIPTokenizer</c> files
/// (<c>vocab.json</c>, <c>merges.txt</c>) are derived from the same merges and give the same ids.
/// </summary>
public sealed partial class ClipTokenizer
{
    public const int ContextLength = 77;
    public const int StartOfText = 49406;
    public const int EndOfText = 49407;

    /// <summary>The merges CLIP uses: the file holds more, and OpenAI's tokenizer keeps the first 48,894.</summary>
    public const int MergeCount = 49152 - 256 - 2;

    private const string EndOfWord = "</w>";

    // GPT-2's reversible map from bytes to printable characters, indexed by byte, and the same characters in the
    // order the vocabulary lists them (the printable bytes first).
    private static readonly (string[] ByByte, string[] InVocabularyOrder) s_bytes = BytesToUnicode();

    private readonly Dictionary<string, int> _ids;
    private readonly Dictionary<(string, string), int> _ranks;
    private readonly ConcurrentDictionary<string, int[]> _cache = new(StringComparer.Ordinal);

    private readonly int _start;
    private readonly int _end;

    private ClipTokenizer(Dictionary<string, int> ids, Dictionary<(string, string), int> ranks)
    {
        _ids = ids;
        _ranks = ranks;
        // 49406 and 49407 with CLIP's merges; taken from the vocabulary so a smaller merges file stays consistent.
        _start = ids["<|startoftext|>"];
        _end = ids["<|endoftext|>"];
    }

    /// <summary>The vocabulary size: 256 bytes, the same with <c>&lt;/w&gt;</c>, one entry per merge, and two special tokens.</summary>
    public int VocabularySize => _ids.Count;

    /// <summary>Reads the gzipped merges file; its first line is a header.</summary>
    public static ClipTokenizer Load(Stream gzipped)
    {
        using var gzip = new GZipStream(gzipped, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        reader.ReadLine();
        var merges = new List<(string, string)>(MergeCount);
        while (merges.Count < MergeCount && reader.ReadLine() is { } line)
        {
            var parts = line.Split(' ');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                throw new InvalidDataException($"Line {merges.Count + 2} of the CLIP merges is not two symbols.");
            }

            merges.Add((parts[0], parts[1]));
        }

        var vocabulary = new List<string>(s_bytes.InVocabularyOrder);
        vocabulary.AddRange(s_bytes.InVocabularyOrder.Select(symbol => symbol + EndOfWord));
        vocabulary.AddRange(merges.Select(merge => merge.Item1 + merge.Item2));
        vocabulary.Add("<|startoftext|>");
        vocabulary.Add("<|endoftext|>");

        var ids = new Dictionary<string, int>(vocabulary.Count, StringComparer.Ordinal);
        for (var i = 0; i < vocabulary.Count; i++)
        {
            ids.TryAdd(vocabulary[i], i);
        }

        var ranks = new Dictionary<(string, string), int>(merges.Count);
        for (var i = 0; i < merges.Count; i++)
        {
            ranks.TryAdd(merges[i], i);
        }

        return new ClipTokenizer(ids, ranks);
    }

    public static ClipTokenizer Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>The ids of <paramref name="text"/> between start and end, without padding.</summary>
    public IReadOnlyList<int> Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ids = new List<int> { _start };
        foreach (Match token in Pattern().Matches(Clean(text)))
        {
            var symbols = string.Concat(Encoding.UTF8.GetBytes(token.Value).Select(b => s_bytes.ByByte[b]));
            ids.AddRange(_cache.GetOrAdd(symbols, Bpe));
        }

        ids.Add(_end);
        return ids;
    }

    /// <summary>
    /// Exactly <see cref="ContextLength"/> ids: start, the text's tokens (cut to fit, keeping the end token last, as
    /// OpenAI's <c>tokenize(truncate=True)</c>), end, then zeros.
    /// </summary>
    public long[] EncodeForModel(string text, out int length)
    {
        var ids = Encode(text).ToList();
        if (ids.Count > ContextLength)
        {
            ids = [.. ids.Take(ContextLength - 1), _end];
        }

        length = ids.Count;
        var padded = new long[ContextLength];
        for (var i = 0; i < ids.Count; i++)
        {
            padded[i] = ids[i];
        }

        return padded;
    }

    // ftfy's repairs are left out: the text is typed by a person or written by the translator, not scraped.
    private static string Clean(string text)
    {
        var unescaped = WebUtility.HtmlDecode(WebUtility.HtmlDecode(text));
        return Whitespace().Replace(unescaped, " ").Trim().ToLowerInvariant();
    }

    private int[] Bpe(string token)
    {
        var word = token.Select(c => c.ToString()).ToList();
        if (word.Count == 0)
        {
            return [];
        }

        word[^1] += EndOfWord;
        while (word.Count > 1)
        {
            var best = -1;
            var bestRank = int.MaxValue;
            for (var i = 0; i < word.Count - 1; i++)
            {
                if (_ranks.TryGetValue((word[i], word[i + 1]), out var rank) && rank < bestRank)
                {
                    (best, bestRank) = (i, rank);
                }
            }

            if (best < 0)
            {
                break;
            }

            // Every occurrence of the best pair is merged, left to right, as CLIP's bpe() does.
            var (first, second) = (word[best], word[best + 1]);
            var merged = new List<string>(word.Count);
            for (var i = 0; i < word.Count; i++)
            {
                if (i < word.Count - 1 && word[i] == first && word[i + 1] == second)
                {
                    merged.Add(first + second);
                    i++;
                }
                else
                {
                    merged.Add(word[i]);
                }
            }

            word = merged;
        }

        // A symbol outside the vocabulary cannot happen with a complete merges file, since every byte is in it.
        return [.. word.Select(symbol => _ids.TryGetValue(symbol, out var id) ? id : throw new InvalidDataException($"'{symbol}' is not in the CLIP vocabulary."))];
    }

    private static (string[] ByByte, string[] InVocabularyOrder) BytesToUnicode()
    {
        var bytes = Enumerable.Range('!', '~' - '!' + 1)
            .Concat(Enumerable.Range('¡', '¬' - '¡' + 1))
            .Concat(Enumerable.Range('®', 'ÿ' - '®' + 1))
            .ToList();
        var characters = bytes.ToList();
        var extra = 0;
        for (var b = 0; b < 256; b++)
        {
            if (!bytes.Contains(b))
            {
                bytes.Add(b);
                characters.Add(256 + extra++);
            }
        }

        var map = new string[256];
        for (var i = 0; i < bytes.Count; i++)
        {
            map[bytes[i]] = char.ConvertFromUtf32(characters[i]);
        }

        // OpenAI's list(bytes_to_unicode().values()) keeps this order: the printable bytes first, then the others.
        return (map, [.. bytes.Select(b => map[b])]);
    }

    [GeneratedRegex(@"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
