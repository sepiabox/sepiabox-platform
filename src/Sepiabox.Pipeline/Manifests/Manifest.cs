using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Manifests;

/// <summary>
/// Everything known about one asset, written once by the pipeline and imported by the cloud (D-014).
/// Schema: <c>docs/schemas/manifest.v1.json</c>.
/// </summary>
public sealed record Manifest
{
    public const int CurrentSchemaVersion = 1;

    public Manifest()
    {
    }

    // For System.Text.Json, which passes both capture fields together whatever their order in the file.
    [JsonConstructor]
    private Manifest(DateTime? capturedAt, CaptureDateSource? capturedAtSource) =>
        Captured = capturedAt is { } value ? new CaptureDate(value, capturedAtSource ?? CaptureDateSource.Metadata) : null;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>SHA-256 of the content, lowercase hex.</summary>
    public required string Id { get; init; }

    public required MediaKind Kind { get; init; }

    public required long SizeBytes { get; init; }

    public required string ContentType { get; init; }

    /// <summary>
    /// When the asset was taken and how that is known (D-046): one value, so a source can never sit next to a missing
    /// date. Written as <c>capturedAt</c> and <c>capturedAtSource</c>.
    /// </summary>
    [JsonIgnore]
    public CaptureDate? Captured { get; init; }

    /// <summary>Wall-clock capture time (D-037); see <see cref="CapturedAtSource"/>.</summary>
    public DateTime? CapturedAt => Captured?.Value;

    /// <summary>
    /// Where <see cref="CapturedAt"/> came from; omitted when there is no date. Manifests written before S-043 never
    /// have it, and their dates all came from metadata, so they read as <see cref="CaptureDateSource.Metadata"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaptureDateSource? CapturedAtSource => Captured?.Source;

    public GeoPoint? Location { get; init; }

    public CameraInfo? Camera { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? DurationSeconds { get; init; }

    public IReadOnlyList<ManifestSource> Sources { get; init; } = [];

    public IReadOnlyList<ManifestDerivative> Derivatives { get; init; } = [];

    public IReadOnlyDictionary<string, JsonNode> Embeddings { get; init; } = new Dictionary<string, JsonNode>();

    /// <summary>
    /// Whether faces were looked for (<c>process --faces</c>, S-018). False means unknown, not "no faces": face
    /// detection is opt-in, so <see cref="Faces"/> is then always empty.
    /// </summary>
    public bool FacesProcessed { get; init; }

    public IReadOnlyList<ManifestFace> Faces { get; init; } = [];
}

/// <summary>One place where the asset's bytes were found.</summary>
/// <param name="Type">Kind of source, such as <c>local</c> for a drive or folder.</param>
/// <param name="Name">Name the user gave the source, such as "HD da Ana".</param>
/// <param name="Path">Path inside the source, with forward slashes.</param>
/// <param name="ModifiedAt">Last write time of the file, UTC.</param>
public sealed record ManifestSource(string Type, string Name, string Path, DateTimeOffset ModifiedAt)
{
    /// <summary>Paths are stored with '/'; a manifest written on Windows by another tool may use '\'.</summary>
    public static string NormalizePath(string path) => path.Replace('\\', '/');

    /// <summary>Same source and same path, whatever the separator. Case matters: Linux drives are case-sensitive.</summary>
    public bool IsSameFile(ManifestSource other) =>
        Type == other.Type
        && Name == other.Name
        && string.Equals(NormalizePath(Path), NormalizePath(other.Path), StringComparison.Ordinal);
}

public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false), new LocalDateTimeConverter() },
    };

    public static string Serialize(Manifest manifest) => JsonSerializer.Serialize(manifest, Options);

    public static Manifest Deserialize(string json) =>
        JsonSerializer.Deserialize<Manifest>(json, Options) ?? throw new JsonException("Manifest is empty.");

    /// <summary>Writes a manifest file through a temporary file, so an interrupted run never leaves half a manifest.</summary>
    public static void WriteFile(string path, Manifest manifest)
    {
        // Unique, so two writers never share a temporary file; flushed to disk before the rename, so a power
        // cut cannot leave an empty manifest under the final name.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file))
            {
                writer.Write(Serialize(manifest));
                writer.Flush();
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next process run, which removes old temporary files from the manifests folder.
        }
    }

    // capturedAt is wall-clock time with no offset, so it must never gain a "Z" or a local offset.
    private sealed class LocalDateTimeConverter : JsonConverter<DateTime>
    {
        private const string Format = "yyyy-MM-ddTHH:mm:ss";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Unspecified);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(Format, System.Globalization.CultureInfo.InvariantCulture));
    }
}
