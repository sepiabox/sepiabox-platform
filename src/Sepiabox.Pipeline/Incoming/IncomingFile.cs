using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Pipeline.Incoming;

/// <summary>Whether the original of a cloud file is kept (S-036, D-088).</summary>
public enum IncomingMode
{
    /// <summary>The worker moves the original to <c>originals</c> (Cold): Sepiabox keeps the file.</summary>
    Custody,

    /// <summary>The original stays at its source: only the manifest and derivatives are stored, like an index-only work folder.</summary>
    Reference,
}

/// <summary>
/// What a blob in the <c>incoming</c> container says about itself (S-036): where the file came from and how to treat it.
/// It travels as the blob's metadata, so the file's bytes are stored as they came. Names, paths and addresses are
/// percent-encoded there, since a blob's metadata values must be ASCII.
/// </summary>
/// <param name="SourceType">The kind of source: <c>gdrive</c>, <c>onedrive</c>, as in the manifest's <c>sources[].type</c>.</param>
/// <param name="SourceName">The source's name (the account's label), as in the manifest.</param>
/// <param name="Path">The file's path in the source, with <c>/</c>; its extension says what kind of file it is.</param>
/// <param name="Mode">Custody or reference.</param>
/// <param name="ModifiedAt">When the source says the file was last modified; null when it does not.</param>
/// <param name="ExternalId">The source's own id for the file (a Drive file id), for the connector that wrote it.</param>
/// <param name="Url">The original's address at its source, when it gives one (<see cref="ManifestSource.Url"/>).</param>
public sealed record IncomingFile(
    string SourceType, string SourceName, string Path, IncomingMode Mode, DateTimeOffset? ModifiedAt = null, string? ExternalId = null, string? Url = null)
{
    public const string SourceTypeKey = "sourcetype";
    public const string SourceNameKey = "sourcename";
    public const string PathKey = "path";
    public const string ModeKey = "mode";
    public const string ModifiedAtKey = "modifiedat";
    public const string ExternalIdKey = "externalid";
    public const string UrlKey = "url";

    /// <summary>The metadata to write with the blob.</summary>
    public IReadOnlyDictionary<string, string> ToMetadata()
    {
        var metadata = new Dictionary<string, string>
        {
            [SourceTypeKey] = Encode(SourceType),
            [SourceNameKey] = Encode(SourceName),
            [PathKey] = Encode(Path),
            [ModeKey] = Mode == IncomingMode.Reference ? "reference" : "custody",
        };
        if (ModifiedAt is { } modified)
        {
            metadata[ModifiedAtKey] = modified.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        if (ExternalId is not null)
        {
            metadata[ExternalIdKey] = Encode(ExternalId);
        }

        if (Url is not null)
        {
            metadata[UrlKey] = Encode(Url);
        }

        return metadata;
    }

    /// <summary>
    /// Reads the metadata of an incoming blob (names are not case sensitive, as a store may change their case). False,
    /// with the reason, when the source, name, path or mode is missing or the mode unknown: the worker cannot know what to do.
    /// </summary>
    public static bool TryRead(IReadOnlyDictionary<string, string> metadata, [NotNullWhen(true)] out IncomingFile? file, [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        file = null;
        var lower = metadata.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value, StringComparer.Ordinal);
        foreach (var key in new[] { SourceTypeKey, SourceNameKey, PathKey, ModeKey })
        {
            if (!lower.TryGetValue(key, out var value) || value.Length == 0)
            {
                problem = $"the incoming blob has no '{key}' metadata.";
                return false;
            }
        }

        IncomingMode mode;
        switch (lower[ModeKey])
        {
            case "custody":
                mode = IncomingMode.Custody;
                break;
            case "reference":
                mode = IncomingMode.Reference;
                break;
            default:
                problem = $"the incoming blob's mode '{lower[ModeKey]}' is neither custody nor reference.";
                return false;
        }

        DateTimeOffset? modified = null;
        if (lower.TryGetValue(ModifiedAtKey, out var text))
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                problem = $"the incoming blob's modifiedat '{text}' is not a date.";
                return false;
            }

            modified = parsed;
        }

        var path = Decode(lower[PathKey]);
        file = new IncomingFile(
            Decode(lower[SourceTypeKey]), Decode(lower[SourceNameKey]), ManifestSource.NormalizePath(path), mode, modified,
            lower.TryGetValue(ExternalIdKey, out var id) ? Decode(id) : null,
            lower.TryGetValue(UrlKey, out var url) ? Decode(url) : null);
        problem = null;
        return true;
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static string Decode(string value) => Uri.UnescapeDataString(value);
}
