using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public sealed class ManifestValidatorTests
{
    private static readonly ManifestValidator s_validator = new();

    [Fact]
    public void Accepts_a_valid_manifest()
    {
        Assert.True(s_validator.TryValidate(ValidJson(), out var manifest, out var error));
        Assert.Equal(TestManifests.PhotoId, manifest.Id);
        Assert.Null(error);
    }

    [Fact]
    public void Rejects_malformed_json()
    {
        Assert.False(s_validator.TryValidate("{ not json", out var manifest, out var error));
        Assert.Null(manifest);
        Assert.StartsWith("not valid JSON", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_schema_violation()
    {
        Assert.False(s_validator.TryValidate(ValidJson(json => json["id"] = "abc"), out _, out var error));
        Assert.Contains("/id", error, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> UndeserializableValues => new()
    {
        { "/sources/0/modifiedAt", "\"yesterday\"" },
        { "/capturedAt", "\"2019-13-45T25:61:61\"" },
        { "/width", "99999999999" },
        { "/width", "1.0" },
    };

    // Each of these used to pass the schema and then throw while deserializing, aborting the whole import.
    [Theory]
    [MemberData(nameof(UndeserializableValues))]
    public void Rejects_values_that_cannot_be_read_without_throwing(string location, string value)
    {
        var json = ValidJson(node => Set(node, location, JsonNode.Parse(value)));

        Assert.False(s_validator.TryValidate(json, out var manifest, out var error));
        Assert.Null(manifest);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Rejects_a_repeated_property_without_throwing()
    {
        var json = ValidJson().Insert(1, "\"kind\":\"video\",");

        Assert.False(s_validator.TryValidate(json, out var manifest, out var error));
        Assert.Null(manifest);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Enforces_date_time_only_on_source_and_face_times_and_a_local_time_pattern_on_capture_time()
    {
        // Format validation is required (RequireFormatValidation), so every "format" is a hard rule.
        // capturedAt deliberately has no "format": date-time would demand an offset the camera never wrote.
        using var stream = typeof(ManifestValidator).Assembly.GetManifestResourceStream("manifest.v1.json")!;
        var schema = JsonNode.Parse(stream)!;

        Assert.Equal(
            [
                "/properties/facesDetectedAt: date-time",
                "/properties/facesErasedAt: date-time",
                "/properties/sources/items/properties/modifiedAt: date-time",
            ],
            Formats(schema, "").Order(StringComparer.Ordinal));
        var capturedAt = schema["properties"]!["capturedAt"]!;
        Assert.Equal(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$", (string?)capturedAt["pattern"]);
        Assert.Null(capturedAt["format"]);
    }

    [Fact]
    public void Rejects_a_source_time_without_an_offset()
    {
        // date-time (RFC 3339) needs an offset; a source time without one would be ambiguous.
        var json = ValidJson(node => node["sources"]![0]!["modifiedAt"] = "2019-07-14T17:00:00");

        Assert.False(s_validator.TryValidate(json, out _, out var error));
        Assert.Contains("/sources/0/modifiedAt", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_json_null()
    {
        Assert.False(s_validator.TryValidate("null", out var manifest, out _));
        Assert.Null(manifest);
    }

    [Theory]
    [InlineData("2019-07-14T16:30:00Z")]
    [InlineData("2019-07-14T16:30:00+01:00")]
    public void Rejects_a_capture_time_with_an_offset(string capturedAt)
    {
        // Camera clocks have no time zone; process never writes one, so an offset means a foreign writer.
        var json = ValidJson(node => node["capturedAt"] = capturedAt);

        Assert.False(s_validator.TryValidate(json, out _, out var error));
        Assert.Contains("/capturedAt", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, CaptureDateSource.Metadata)]
    [InlineData("metadata", CaptureDateSource.Metadata)]
    [InlineData("fileName", CaptureDateSource.FileName)]
    [InlineData("folder", CaptureDateSource.Folder)]
    public void Accepts_a_capture_date_source_when_it_is_known_or_absent(string? value, CaptureDateSource expected)
    {
        // Absent: a manifest written before S-043, whose date came from metadata.
        var json = ValidJson(node =>
        {
            node.Remove("capturedAtSource");
            if (value is not null)
            {
                node["capturedAtSource"] = value;
            }
        });

        Assert.True(s_validator.TryValidate(json, out var manifest, out var error), error);
        Assert.Equal(expected, manifest.CapturedAtSource);
        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 0), manifest.CapturedAt);
    }

    [Theory]
    [InlineData("exif")]
    [InlineData("FileName")]
    [InlineData("")]
    public void Rejects_a_capture_date_source_when_it_is_unknown(string value)
    {
        var json = ValidJson(node => node["capturedAtSource"] = value);

        Assert.False(s_validator.TryValidate(json, out _, out var error));
        Assert.Contains("/capturedAtSource", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_capture_date_source_when_there_is_no_capture_date()
    {
        var json = ValidJson(node =>
        {
            node["capturedAt"] = null;
            node["capturedAtSource"] = "folder";
        });

        Assert.False(s_validator.TryValidate(json, out _, out var error));
        Assert.Contains("/capturedAt", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_a_clip_embedding_of_512_float32_values()
    {
        var json = ValidJson(node => node["embeddings"] = new JsonObject
        {
            ["clip-vit-b32"] = EmbeddingCodec.Encode(new float[ClipImageEmbedder.Dimensions]),
        });

        Assert.True(s_validator.TryValidate(json, out var manifest, out var error), error);
        Assert.True(manifest.Embeddings.ContainsKey("clip-vit-b32"));
    }

    [Theory]
    [InlineData(511)]
    [InlineData(513)]
    public void Rejects_a_clip_embedding_of_the_wrong_length(int values)
    {
        var json = ValidJson(node => node["embeddings"] = new JsonObject
        {
            ["clip-vit-b32"] = EmbeddingCodec.Encode(new float[values]),
        });

        Assert.False(s_validator.TryValidate(json, out _, out var error));
        Assert.Contains("/embeddings/clip-vit-b32", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_embedding_that_is_not_base64()
    {
        var json = ValidJson(node => node["embeddings"] = new JsonObject { ["other-model"] = "not base64!" });

        Assert.False(s_validator.TryValidate(json, out _, out _));
    }

    [Fact]
    public void Rejects_a_clip_embedding_of_the_right_length_that_is_not_base64()
    {
        var json = ValidJson(node => node["embeddings"] = new JsonObject { ["clip-vit-b32"] = new string('!', 2732) });

        Assert.False(s_validator.TryValidate(json, out _, out var error));
        Assert.Contains("/embeddings/clip-vit-b32", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_detected_faces_when_faces_were_processed()
    {
        var json = ValidJson(node =>
        {
            node["facesProcessed"] = true;
            node["faces"] = new JsonArray(Face());
        });

        Assert.True(s_validator.TryValidate(json, out var manifest, out var error), error);
        var face = Assert.Single(manifest.Faces);
        Assert.Equal([0.25, 0.1, 0.2, 0.3], face.Box);
        Assert.Equal(0.97, face.Score);
        Assert.Equal(128, EmbeddingCodec.Decode(face.Embedding).Length);
    }

    [Fact]
    public void Accepts_a_manifest_written_before_faces_with_faces_not_processed_and_none_listed()
    {
        Assert.True(s_validator.TryValidate(ValidJson(), out var manifest, out var error), error);
        Assert.False(manifest.FacesProcessed);
        Assert.Empty(manifest.Faces);
    }

    [Fact]
    public void Accepts_a_faces_version_next_to_processed_faces_and_reads_a_missing_one_as_1()
    {
        var json = ValidJson(node =>
        {
            node["facesProcessed"] = true;
            node["facesVersion"] = 2;
        });

        Assert.True(s_validator.TryValidate(json, out var versioned, out var error), error);
        Assert.Equal(2, versioned.EffectiveFacesVersion);
        Assert.True(s_validator.TryValidate(ValidJson(node => node["facesProcessed"] = true), out var unversioned, out error), error);
        Assert.Null(unversioned.FacesVersion);
        Assert.Equal(1, unversioned.EffectiveFacesVersion);
    }

    [Theory]
    [InlineData(false, "1")]
    [InlineData(true, "0")]
    [InlineData(true, "1.5")]
    [InlineData(true, "\"1\"")]
    public void Rejects_a_faces_version_that_is_not_a_positive_integer_next_to_processed_faces(bool processed, string version)
    {
        var json = ValidJson(node =>
        {
            node["facesProcessed"] = processed;
            node["facesVersion"] = JsonNode.Parse(version);
        });

        Assert.False(s_validator.TryValidate(json, out _, out _));
    }

    [Fact]
    public void Rejects_faces_when_faces_were_not_processed()
    {
        var json = ValidJson(node => node["faces"] = new JsonArray(Face()));

        Assert.False(s_validator.TryValidate(json, out _, out var error));
        Assert.Contains("/faces", error, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> InvalidFaces => new()
    {
        { "box", "[0.1, 0.2, 0.3]" },
        { "box", "[0.1, 0.2, 0.3, 1.5]" },
        { "box", "[-0.1, 0.2, 0.3, 0.4]" },
        { "score", "1.2" },
        { "score", "\"high\"" },
        { "embedding", "\"AAAA\"" },
        { "unknown", "1" },
    };

    [Theory]
    [MemberData(nameof(InvalidFaces))]
    public void Rejects_a_face_that_does_not_match_the_schema(string property, string value)
    {
        var json = ValidJson(node =>
        {
            node["facesProcessed"] = true;
            var face = Face();
            face[property] = JsonNode.Parse(value);
            node["faces"] = new JsonArray(face);
        });

        Assert.False(s_validator.TryValidate(json, out _, out var error));
        Assert.Contains("/faces/0", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_face_without_an_embedding()
    {
        var json = ValidJson(node =>
        {
            node["facesProcessed"] = true;
            var face = Face();
            face.Remove("embedding");
            node["faces"] = new JsonArray(face);
        });

        Assert.False(s_validator.TryValidate(json, out _, out _));
    }

    private static JsonObject Face()
    {
        var embedding = new float[128];
        embedding[0] = 1;
        return new JsonObject
        {
            ["box"] = new JsonArray(0.25, 0.1, 0.2, 0.3),
            ["score"] = 0.97,
            ["embedding"] = EmbeddingCodec.Encode(embedding),
        };
    }

    private static string ValidJson(Action<JsonObject>? change = null)
    {
        var node = JsonNode.Parse(ManifestJson.Serialize(TestManifests.Photo(TestManifests.Source("HD da Ana", "2019/praia.jpg"))))!.AsObject();
        change?.Invoke(node);
        return node.ToJsonString();
    }

    private static List<string> Formats(JsonNode node, string location)
    {
        var found = new List<string>();
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    if (name == "format" && child is JsonValue value)
                    {
                        found.Add($"{location}: {value}");
                    }
                    else if (child is not null)
                    {
                        found.AddRange(Formats(child, $"{location}/{name}"));
                    }
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is { } item)
                    {
                        found.AddRange(Formats(item, $"{location}/{index}"));
                    }
                }

                break;
        }

        return found;
    }

    private static void Set(JsonObject root, string location, JsonNode? value)
    {
        var segments = location.Trim('/').Split('/');
        JsonNode current = root;
        foreach (var segment in segments[..^1])
        {
            current = int.TryParse(segment, out var index) ? current[index]! : current[segment]!;
        }

        current.AsObject()[segments[^1]] = value;
    }
}
