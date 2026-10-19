using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Manifests;

/// <summary>
/// Everything known about one asset, written once by the pipeline and imported by the cloud (D-014).
/// Schema: <c>docs/schemas/manifest.v1.json</c>. Later stories fill <see cref="Embeddings"/>
/// and <see cref="Faces"/>.
/// </summary>
public sealed record Manifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>SHA-256 of the content, lowercase hex.</summary>
    public required string Id { get; init; }

    public required MediaKind Kind { get; init; }

    public required long SizeBytes { get; init; }

    public required string ContentType { get; init; }

    /// <summary>Local time on the camera's clock; cameras rarely record the time zone.</summary>
    public DateTime? CapturedAt { get; init; }

    public GeoPoint? Location { get; init; }

    public CameraInfo? Camera { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? DurationSeconds { get; init; }

    public IReadOnlyList<ManifestSource> Sources { get; init; } = [];

    public IReadOnlyList<ManifestDerivative> Derivatives { get; init; } = [];

    public IReadOnlyDictionary<string, JsonNode> Embeddings { get; init; } = new Dictionary<string, JsonNode>();

    public bool FacesProcessed { get; init; }

    public IReadOnlyList<JsonObject> Faces { get; init; } = [];
}

/// <summary>One place where the asset's bytes were found.</summary>
/// <param name="Type">Kind of source, such as <c>local</c> for a drive or folder.</param>
/// <param name="Name">Name the user gave the source, such as "HD da Ana".</param>
/// <param name="Path">Path inside the source, with forward slashes.</param>
/// <param name="ModifiedAt">Last write time of the file, UTC.</param>
public sealed record ManifestSource(string Type, string Name, string Path, DateTimeOffset ModifiedAt)
{
    public bool IsSameFile(ManifestSource other) =>
        Type == other.Type && Name == other.Name && string.Equals(Path, other.Path, StringComparison.Ordinal);
}

public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new LocalDateTimeConverter() },
    };

    public static string Serialize(Manifest manifest) => JsonSerializer.Serialize(manifest, Options);

    public static Manifest Deserialize(string json) =>
        JsonSerializer.Deserialize<Manifest>(json, Options) ?? throw new JsonException("Manifest is empty.");

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
