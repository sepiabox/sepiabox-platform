using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Sepiabox.Pipeline.Models;

namespace Sepiabox.Pipeline.Embeddings;

/// <summary>
/// CLIP ViT-B/32 image embeddings (D-016): 512 values, L2-normalized, stored in manifests under
/// <see cref="ModelKey"/>. One instance per run; ONNX Runtime sessions are expensive to create.
/// </summary>
public sealed class ClipImageEmbedder : IImageEmbedder, IDisposable
{
    public const string ModelKey = "clip-vit-b32";

    public const int Dimensions = 512;

    /// <summary>The projected image embedding, next to the raw hidden states in the Xenova export.</summary>
    public const string OutputName = "image_embeds";

    private readonly InferenceSession _session;
    private readonly string _input;

    private ClipImageEmbedder(InferenceSession session)
    {
        _session = session;
        _input = session.InputMetadata.Keys.First();
    }

    string IImageEmbedder.ModelKey => ModelKey;

    int IImageEmbedder.Dimensions => Dimensions;

    /// <summary>The pinned CLIP model in <paramref name="modelFolder"/>; see the other overload.</summary>
    public static ClipImageEmbedder? TryCreate(string modelFolder, out string? warning) =>
        TryCreate(modelFolder, ModelCatalog.Get(ModelCatalog.ClipVision), out warning);

    /// <summary>
    /// Loads <paramref name="model"/> from <paramref name="modelFolder"/>, or returns null with a warning that
    /// says why: the file is missing, its SHA-256 is not the pinned one, ONNX Runtime cannot load it, or it has
    /// no <see cref="OutputName"/> output. A run goes on without embeddings rather than failing every photo.
    /// </summary>
    public static ClipImageEmbedder? TryCreate(string modelFolder, ModelInfo model, out string? warning)
    {
        const string Download =
            "Run `sepiabox-ingest models download` and process again: embeddings are added from the work folder "
            + "without reading the drive again.";
        var session = ModelSession.TryOpen(
            modelFolder, model, "The CLIP model", "so no embeddings were computed", Download, [OutputName], out warning);
        return session is null ? null : new ClipImageEmbedder(session);
    }

    public float[] Embed(string path)
    {
        var tensor = new DenseTensor<float>(ClipPreprocessor.Prepare(path), [1, 3, ClipPreprocessor.Size, ClipPreprocessor.Size]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, tensor)], [OutputName]);
        var embedding = results[0].AsEnumerable<float>().ToArray();
        if (embedding.Length != Dimensions)
        {
            throw new EmbeddingException($"The CLIP model returned {embedding.Length} values instead of {Dimensions}.");
        }

        EmbeddingCodec.Normalize(embedding);
        return embedding;
    }

    public void Dispose() => _session.Dispose();
}
