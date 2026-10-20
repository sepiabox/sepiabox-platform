namespace Sepiabox.Pipeline.Embeddings;

/// <summary>A model output that cannot be stored: wrong size, values that are not finite, or no direction.</summary>
public sealed class EmbeddingException(string message) : Exception(message);
