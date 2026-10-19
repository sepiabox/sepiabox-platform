using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Manifests;

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
    public void Enforces_date_time_only_on_source_times_and_a_local_time_pattern_on_capture_time()
    {
        // Format validation is required (RequireFormatValidation), so every "format" is a hard rule.
        // capturedAt deliberately has no "format": date-time would demand an offset the camera never wrote.
        using var stream = typeof(ManifestValidator).Assembly.GetManifestResourceStream("manifest.v1.json")!;
        var schema = JsonNode.Parse(stream)!;

        Assert.Equal(["/properties/sources/items/properties/modifiedAt: date-time"], Formats(schema, ""));
        var capturedAt = schema["properties"]!["capturedAt"]!;
        Assert.Equal(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$", (string?)capturedAt["pattern"]);
        Assert.Null(capturedAt["format"]);
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

    private static string ValidJson(Action<JsonObject>? change = null)
    {
        var node = JsonNode.Parse(ManifestJson.Serialize(TestManifests.Photo(TestManifests.Source("HD da Ana", "2019/praia.jpg"))))!.AsObject();
        change?.Invoke(node);
        return node.ToJsonString();
    }

    private static List<string> Formats(JsonNode node, string location)
    {
        var found = new List<string>();
        if (node is JsonObject obj)
        {
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
