using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Sepiabox.Search.Models;

namespace Sepiabox.Search.Clip;

/// <summary>
/// CLIP ViT-B/32 text embeddings (S-016): 512 values, L2-normalized, in the same space as the image embeddings
/// <c>process</c> stores (D-016), so a description can be compared with every photo by cosine similarity.
/// One instance per process: ONNX Runtime sessions are expensive to create and safe to share.
/// </summary>
public sealed class ClipTextEncoder : IDisposable
{
    public const int Dimensions = 512;

    /// <summary>The projected text embedding, next to the raw hidden states in the Xenova export.</summary>
    public const string OutputName = "text_embeds";

    public const string InputIds = "input_ids";

    private const string AttentionMask = "attention_mask";

    private readonly InferenceSession _session;
    private readonly ClipTokenizer _tokenizer;
    private readonly bool _takesMask;

    private ClipTextEncoder(InferenceSession session, ClipTokenizer tokenizer)
    {
        _session = session;
        _tokenizer = tokenizer;
        _takesMask = session.InputMetadata.ContainsKey(AttentionMask);
    }

    public ClipTokenizer Tokenizer => _tokenizer;

    /// <summary>True when both files are pinned and in <paramref name="modelFolder"/>; says nothing of their content.</summary>
    public static bool FilesPresent(string modelFolder) =>
        ModelCatalog.Find(ModelCatalog.ClipText) is { } text && ModelCatalog.Find(ModelCatalog.ClipBpe) is { } bpe
        && File.Exists(ModelFolder.PathOf(modelFolder, text)) && File.Exists(ModelFolder.PathOf(modelFolder, bpe));

    /// <summary>
    /// The text encoder from <paramref name="modelFolder"/>, or null with a warning that says why: a file is not pinned,
    /// missing, or not the pinned one, or ONNX Runtime cannot load it. Never throws for any of those.
    /// </summary>
    public static ClipTextEncoder? TryCreate(string modelFolder, out string? warning)
    {
        const string Consequence = "so search by description is off";
        const string Remedy = "Run `sepiabox-ingest models download`, then restart.";
        if (ModelCatalog.Find(ModelCatalog.ClipText) is not { } textModel || ModelCatalog.Find(ModelCatalog.ClipBpe) is not { } bpe)
        {
            warning = $"models.json does not pin the CLIP text model and its tokenizer, {Consequence}.";
            return null;
        }

        if (TryLoadTokenizer(modelFolder, bpe, Consequence, Remedy, out warning) is not { } tokenizer)
        {
            return null;
        }

        var session = ModelSession.TryOpen(
            modelFolder, textModel, "The CLIP text model", Consequence, Remedy, [new ModelInput(InputIds)], [OutputName], out warning);
        return session is null ? null : new ClipTextEncoder(session, tokenizer);
    }

    /// <summary>The tokenizer alone, verified against its pin; null with a warning otherwise.</summary>
    public static ClipTokenizer? TryLoadTokenizer(string modelFolder, out string? warning) =>
        ModelCatalog.Find(ModelCatalog.ClipBpe) is { } bpe
            ? TryLoadTokenizer(modelFolder, bpe, "so text cannot be tokenized", "Run `sepiabox-ingest models download`.", out warning)
            : Fail("models.json does not pin the CLIP tokenizer.", out warning);

    /// <summary>The embedding of <paramref name="text"/>: 512 values with norm 1.</summary>
    public float[] Encode(string text)
    {
        var ids = _tokenizer.EncodeForModel(text, out var length);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputIds, new DenseTensor<long>(ids, [1, ClipTokenizer.ContextLength])),
        };
        if (_takesMask)
        {
            var mask = new long[ClipTokenizer.ContextLength];
            Array.Fill(mask, 1L, 0, length);
            inputs.Add(NamedOnnxValue.CreateFromTensor(AttentionMask, new DenseTensor<long>(mask, [1, ClipTokenizer.ContextLength])));
        }

        using var results = _session.Run(inputs, [OutputName]);
        var embedding = results[0].AsEnumerable<float>().ToArray();
        if (embedding.Length != Dimensions)
        {
            throw new InvalidOperationException($"The CLIP text model returned {embedding.Length} values instead of {Dimensions}.");
        }

        Normalize(embedding);
        return embedding;
    }

    public void Dispose() => _session.Dispose();

    /// <summary>Scales <paramref name="vector"/> to length 1 in place; a vector of length 0 or not finite is refused.</summary>
    public static void Normalize(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm == 0 || !double.IsFinite(norm))
        {
            throw new InvalidOperationException("The CLIP text model returned a vector that cannot be normalized.");
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }

    private static ClipTokenizer? TryLoadTokenizer(string modelFolder, ModelInfo bpe, string consequence, string remedy, out string? warning)
    {
        var path = ModelFolder.PathOf(modelFolder, bpe);
        if (!File.Exists(path))
        {
            return Fail($"The CLIP tokenizer is not in {modelFolder}, {consequence}. {remedy}", out warning);
        }

        try
        {
            if (!ModelVerifier.Verify(bpe, path, trustMarker: true, out var actual, out _))
            {
                return Fail($"{path} has SHA-256 {actual}, not the {bpe.Sha256} pinned in models.json, {consequence}. {remedy}", out warning);
            }

            warning = null;
            return ClipTokenizer.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Fail($"Could not read {path} ({ex.Message}), {consequence}. {remedy}", out warning);
        }
    }

    private static ClipTokenizer? Fail(string message, out string? warning)
    {
        warning = message;
        return null;
    }
}
