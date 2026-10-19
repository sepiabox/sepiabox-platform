using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Pipeline.Manifests;

/// <summary>Reads a manifest written by someone else, or explains why it was rejected.</summary>
public interface IManifestValidator
{
    bool TryValidate(string json, [NotNullWhen(true)] out Manifest? manifest, [NotNullWhen(false)] out string? problem);
}
