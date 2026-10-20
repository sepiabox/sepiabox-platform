using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class ManifestTests
{
    private static readonly Manifest Sample = new()
    {
        Id = new string('a', 64),
        Kind = MediaKind.Photo,
        SizeBytes = 2048,
        ContentType = "image/jpeg",
        CapturedAt = new DateTime(2019, 7, 14, 16, 30, 5, DateTimeKind.Local),
        Location = new GeoPoint(41.18, -8.69),
        Camera = new CameraInfo("Apple", "iPhone 8"),
        Width = 4032,
        Height = 3024,
        Sources = [new ManifestSource("local", "HD da Ana", "2019/praia.jpg", new DateTimeOffset(2019, 7, 15, 10, 0, 0, TimeSpan.Zero))],
    };

    [Fact]
    public void Writes_the_v1_field_names_and_values()
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(Sample))!.AsObject();

        Assert.Equal(1, (int)json["schemaVersion"]!);
        Assert.Equal("photo", (string)json["kind"]!);
        Assert.Equal("2019-07-14T16:30:05", (string)json["capturedAt"]!);
        Assert.Equal(41.18, (double)json["location"]!["latitude"]!);
        Assert.Equal("iPhone 8", (string)json["camera"]!["model"]!);
        Assert.Null(json["durationSeconds"]);
        Assert.True(json.ContainsKey("durationSeconds"));
        Assert.Equal("HD da Ana", (string)json["sources"]![0]!["name"]!);
        Assert.Empty(json["derivatives"]!.AsArray());
        Assert.Empty(json["embeddings"]!.AsObject());
        Assert.False((bool)json["facesProcessed"]!);
    }

    [Fact]
    public void Omits_the_capture_date_source_when_it_is_not_known()
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(Sample with { CapturedAt = null, CapturedAtSource = null }))!.AsObject();

        Assert.False(json.ContainsKey("capturedAtSource"));
        Assert.True(json.ContainsKey("capturedAt"));
    }

    [Fact]
    public void Drops_the_capture_date_source_when_there_is_no_capture_date()
    {
        var undated = Sample with { CapturedAtSource = CaptureDateSource.Folder, CapturedAt = null };

        Assert.Null(undated.CapturedAtSource);
        Assert.False(JsonNode.Parse(ManifestJson.Serialize(undated))!.AsObject().ContainsKey("capturedAtSource"));
    }

    [Theory]
    [InlineData(CaptureDateSource.Metadata, "metadata")]
    [InlineData(CaptureDateSource.FileName, "fileName")]
    [InlineData(CaptureDateSource.Folder, "folder")]
    public void Writes_the_capture_date_source_when_it_is_known(CaptureDateSource source, string expected)
    {
        var json = ManifestJson.Serialize(Sample with { CapturedAtSource = source });

        Assert.Equal(expected, (string?)JsonNode.Parse(json)!["capturedAtSource"]);
        Assert.Equal(source, ManifestJson.Deserialize(json).CapturedAtSource);
    }

    [Fact]
    public void Reads_back_what_it_writes()
    {
        var copy = ManifestJson.Deserialize(ManifestJson.Serialize(Sample));

        Assert.Equal(Sample.CapturedAt, copy.CapturedAt);
        Assert.Equal(Sample.Location, copy.Location);
        Assert.Equal(Sample.Camera, copy.Camera);
        Assert.Equal(Sample.Sources, copy.Sources);
        Assert.Equal(Sample.Kind, copy.Kind);
    }

    [Fact]
    public void Treats_sources_with_the_same_type_name_and_path_as_the_same_file()
    {
        var source = Sample.Sources[0];

        Assert.True(source.IsSameFile(source with { ModifiedAt = DateTimeOffset.UnixEpoch }));
        Assert.False(source.IsSameFile(source with { Path = "2019/other.jpg" }));
    }
}
