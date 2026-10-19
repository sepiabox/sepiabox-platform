using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Sepiabox.Pipeline.Models;

namespace Sepiabox.Pipeline.Embeddings;

/// <summary>
/// CLIP ViT-B/32 image embeddings (D-016): 512 values, L2-normalized, stored in manifests under
/// <see cref="ModelKey"/>. One instance per run; ONNX Runtime sessions are expensive to create.
/// </summary>
public sealed class ClipImageEmbedder : IDisposable
{
    /// <summary>Key in the manifest's <c>embeddings</c> and the catalog's <c>model</c> column.</summary>
    public const string ModelKey = "clip-vit-b32";

    public const int Dimensions = 512;

    private readonly InferenceSession _session;
    private readonly string _input;
    private readonly string _output;

    private ClipImageEmbedder(InferenceSession session, string? warning)
    {
        _session = session;
        _input = session.InputMetadata.Keys.First();
        // The Xenova export has the projected embedding as "image_embeds" next to the raw hidden states.
        _output = session.OutputMetadata.ContainsKey("image_embeds") ? "image_embeds" : session.OutputMetadata.Keys.First();
        Warning = warning;
    }

    /// <summary>Set when CUDA was asked for but could not be used, so the run fell back to the CPU.</summary>
    public string? Warning { get; }

    /// <summary>Null when the model has not been downloaded to <paramref name="modelFolder"/>.</summary>
    public static ClipImageEmbedder? TryCreate(string modelFolder, bool useGpu)
    {
        var path = ModelFolder.PathOf(modelFolder, ModelCatalog.Get(ModelCatalog.ClipVision));
        if (!File.Exists(path))
        {
            return null;
        }

        string? warning = null;
        var options = new SessionOptions();
        if (useGpu)
        {
            try
            {
                options.AppendExecutionProvider_CUDA();
            }
            catch (OnnxRuntimeException ex)
            {
                // The default build ships the CPU provider only (D-042); a missing GPU must not stop a run.
                warning = $"CUDA is not available ({ex.Message}); embeddings are computed on the CPU.";
                options.Dispose();
                options = new SessionOptions();
            }
        }

        try
        {
            return new ClipImageEmbedder(new InferenceSession(path, options), warning);
        }
        finally
        {
            options.Dispose();
        }
    }

    /// <summary>The embedding of the image at <paramref name="path"/>, L2-normalized.</summary>
    public float[] Embed(string path)
    {
        var tensor = new DenseTensor<float>(ClipPreprocessor.Prepare(path), [1, 3, ClipPreprocessor.Size, ClipPreprocessor.Size]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, tensor)], [_output]);
        var embedding = results[0].AsEnumerable<float>().ToArray();
        if (embedding.Length != Dimensions)
        {
            throw new InvalidOperationException($"The CLIP model returned {embedding.Length} values instead of {Dimensions}.");
        }

        EmbeddingCodec.Normalize(embedding);
        return embedding;
    }

    public void Dispose() => _session.Dispose();
}
