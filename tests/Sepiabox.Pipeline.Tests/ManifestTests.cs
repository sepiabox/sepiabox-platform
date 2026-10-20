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
        Captured = new(new DateTime(2019, 7, 14, 16, 30, 5, DateTimeKind.Local), CaptureDateSource.Metadata),
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
    public void Omits_the_capture_date_source_when_there_is_no_capture_date()
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(Sample with { Captured = null }))!.AsObject();

        Assert.False(json.ContainsKey("capturedAtSource"));
        Assert.True(json.ContainsKey("capturedAt"));
        Assert.Null(json["capturedAt"]);
    }

    [Fact]
    public void Does_not_bring_back_a_source_when_a_date_is_removed_and_set_again()
    {
        var undated = Sample with { Captured = Sample.Captured! with { Source = CaptureDateSource.Folder } } with { Captured = null };

        var redated = undated with { Captured = new(new DateTime(2020, 1, 1), CaptureDateSource.FileName) };

        Assert.Null(undated.CapturedAtSource);
        Assert.Equal(CaptureDateSource.FileName, redated.CapturedAtSource);
    }

    [Fact]
    public void Treats_manifests_as_equal_when_their_public_state_is_the_same()
    {
        var once = Sample with { Captured = null };
        var dateRemoved = (Sample with { Captured = new(new DateTime(2008, 12, 24), CaptureDateSource.Folder) }) with { Captured = null };

        Assert.Equal(once, dateRemoved);
        Assert.Equal(once.GetHashCode(), dateRemoved.GetHashCode());
    }

    [Fact]
    public void Ignores_a_capture_date_source_without_a_capture_date_when_reading()
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(Sample with { Captured = null }))!.AsObject();
        json["capturedAtSource"] = "folder";

        var manifest = ManifestJson.Deserialize(json.ToJsonString());

        Assert.Null(manifest.Captured);
        Assert.Null(manifest.CapturedAtSource);
    }

    [Fact]
    public void Reads_the_capture_date_and_its_source_in_any_order_when_reading()
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(Sample with { Captured = null }))!.AsObject();
        json.Remove("capturedAt");
        json["capturedAtSource"] = "fileName";
        json["capturedAt"] = "2012-03-04T10:11:12";

        var manifest = ManifestJson.Deserialize(json.ToJsonString());

        Assert.Equal(new CaptureDate(new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName), manifest.Captured);
    }

    [Fact]
    public void Reads_a_date_without_a_source_as_metadata_when_the_manifest_predates_the_field()
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(Sample))!.AsObject();
        json.Remove("capturedAtSource");

        Assert.Equal(CaptureDateSource.Metadata, ManifestJson.Deserialize(json.ToJsonString()).CapturedAtSource);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    public void Rejects_a_number_for_the_capture_date_source_when_reading(string number)
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(Sample))!.AsObject();
        json["capturedAtSource"] = JsonNode.Parse(number);

        Assert.Throws<System.Text.Json.JsonException>(() => ManifestJson.Deserialize(json.ToJsonString()));
    }

    [Theory]
    [InlineData(CaptureDateSource.Metadata, "metadata")]
    [InlineData(CaptureDateSource.FileName, "fileName")]
    [InlineData(CaptureDateSource.Folder, "folder")]
    public void Writes_the_capture_date_source_when_it_is_known(CaptureDateSource source, string expected)
    {
        var json = ManifestJson.Serialize(Sample with { Captured = Sample.Captured! with { Source = source } });

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
