namespace Sepiabox.Pipeline.Embeddings;

/// <summary>Turns an image into a vector that can be compared with others by cosine similarity.</summary>
public interface IImageEmbedder
{
    /// <summary>Key in the manifest's <c>embeddings</c> and the catalog's <c>model</c> column.</summary>
    string ModelKey { get; }

    int Dimensions { get; }

    /// <summary>
    /// The L2-normalized embedding of the image at <paramref name="path"/>. Throws
    /// <see cref="EmbeddingException"/> when the model gives no usable vector.
    /// </summary>
    float[] Embed(string path);
}

/// <summary>A model output that cannot be stored: wrong size, values that are not finite, or no direction.</summary>
public sealed class EmbeddingException(string message) : Exception(message);
