namespace Sepiabox.Pipeline.Manifests;

/// <summary>A file generated from the original for the web: a thumbnail, a preview or a video version.</summary>
/// <param name="Name">Stable name the web asks for, such as <c>thumb-256</c>.</param>
/// <param name="Path">Path relative to the work folder, with forward slashes: <c>derivatives/&lt;id&gt;/thumb-256.webp</c>.</param>
/// <param name="Width">Width in pixels, after orientation.</param>
/// <param name="Height">Height in pixels, after orientation.</param>
/// <param name="ContentType">MIME type, such as <c>image/webp</c>.</param>
public sealed record ManifestDerivative(string Name, string Path, int Width, int Height, string ContentType);
