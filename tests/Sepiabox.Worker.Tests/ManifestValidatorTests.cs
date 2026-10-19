using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Tests;

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
