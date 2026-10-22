using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Sepiabox.Search.Models;

namespace Sepiabox.Search.Clip;

/// <summary>
/// CLIP ViT-B/32 image embeddings of an image already cut and resized to the model's input (S-041, D-099): the web
/// compares a region of a photo with the stored embeddings, which <c>process</c> computed from whole photos with the
/// same model and preprocessing (D-016). One instance per process; ONNX Runtime sessions are safe to share.
/// </summary>
public sealed class ClipImageEncoder : IDisposable
{
    public const int Dimensions = 512;

    /// <summary>The projected image embedding, next to the raw hidden states in the Xenova export.</summary>
    public const string OutputName = "image_embeds";

    private readonly InferenceSession _session;
    private readonly string _input;

    private ClipImageEncoder(InferenceSession session)
    {
        _session = session;
        _input = session.InputMetadata.Keys.First();
    }

    /// <summary>True when the pinned vision model is in <paramref name="modelFolder"/>; says nothing of its content.</summary>
    public static bool FilesPresent(string modelFolder) =>
        ModelCatalog.Find(ModelCatalog.ClipVision) is { } model && File.Exists(ModelFolder.PathOf(modelFolder, model));

    /// <summary>
    /// The encoder from <paramref name="modelFolder"/>, or null with a warning that says why and a failure that says
    /// whether trying again may help. Never throws for a missing, unpinned, unreadable or unusable model.
    /// </summary>
    public static ClipImageEncoder? TryCreate(string modelFolder, out string? warning, out ClipLoadFailure failure)
    {
        const string Consequence = "so search by a region of a photo is off";
        const string Remedy = "Run `sepiabox-ingest models download`, then restart.";
        if (ModelCatalog.Find(ModelCatalog.ClipVision) is not { } model)
        {
            (warning, failure) = ($"models.json does not pin the CLIP image model, {Consequence}.", ClipLoadFailure.NotPinned);
            return null;
        }

        var path = ModelFolder.PathOf(modelFolder, model);
        if (!File.Exists(path))
        {
            (warning, failure) = ($"The CLIP image model is not in {modelFolder}, {Consequence}. {Remedy}", ClipLoadFailure.Missing);
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (warning, failure) = ($"Could not open {path} ({ex.Message}), {Consequence}.", ClipLoadFailure.Unreadable);
            return null;
        }

        var session = ModelSession.TryOpen(modelFolder, model, "The CLIP image model", Consequence, Remedy, [], [OutputName], out warning);
        failure = session is null ? ClipLoadFailure.Invalid : ClipLoadFailure.None;
        return session is null ? null : new ClipImageEncoder(session);
    }

    /// <summary>The embedding of a 224×224 image given as interleaved RGB bytes (<see cref="ClipPreprocessor.ResizeAndCrop"/>): 512 values with norm 1.</summary>
    public float[] Encode(ReadOnlySpan<byte> rgb)
    {
        var tensor = new DenseTensor<float>(ClipPreprocessor.ToTensor(rgb), [1, 3, ClipPreprocessor.Size, ClipPreprocessor.Size]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, tensor)], [OutputName]);
        var embedding = results[0].AsEnumerable<float>().ToArray();
        if (embedding.Length != Dimensions)
        {
            throw new InvalidOperationException($"The CLIP image model returned {embedding.Length} values instead of {Dimensions}.");
        }

        ClipTextEncoder.Normalize(embedding);
        return embedding;
    }

    public void Dispose() => _session.Dispose();
}
