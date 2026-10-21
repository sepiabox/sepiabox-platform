using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

/// <summary>The manifest's index-only fields (S-063, D-088): <c>originalStored</c> and a source's <c>url</c>.</summary>
public sealed class IndexOnlyManifestTests
{
    private static readonly ManifestValidator s_validator = new();
    private static readonly DateTimeOffset Noon = new(2019, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Writes_original_stored_only_when_it_is_false()
    {
        var kept = JsonNode.Parse(ManifestJson.Serialize(Photo(Source("HD"))))!.AsObject();
        var indexOnly = JsonNode.Parse(ManifestJson.Serialize(Photo(Source("HD")) with { OriginalStored = false }))!.AsObject();

        Assert.False(kept.ContainsKey("originalStored"));
        Assert.False(kept["sources"]![0]!.AsObject().ContainsKey("url"));
        Assert.False(indexOnly["originalStored"]!.GetValue<bool>());
    }

    [Fact]
    public void Reads_a_manifest_without_original_stored_as_stored()
    {
        var manifest = ManifestJson.Deserialize(ManifestJson.Serialize(Photo(Source("HD"))));

        Assert.True(manifest.IsOriginalStored);
        Assert.Null(manifest.OriginalStored);
        Assert.Null(Assert.Single(manifest.Sources).Url);
    }

    [Fact]
    public void Round_trips_original_stored_false_and_a_source_url()
    {
        var written = Photo(Source("Takeout") with { Url = "https://photos.example.com/p/1" }) with { OriginalStored = false };

        var read = ManifestJson.Deserialize(ManifestJson.Serialize(written));

        Assert.False(read.IsOriginalStored);
        Assert.Equal("https://photos.example.com/p/1", Assert.Single(read.Sources).Url);
    }

    [Fact]
    public void The_schema_accepts_a_manifest_with_and_without_the_new_fields()
    {
        Assert.True(s_validator.TryValidate(Json(), out _, out var error), error);
        Assert.True(s_validator.TryValidate(Json(json => json["originalStored"] = false), out var manifest, out error), error);
        Assert.False(manifest.IsOriginalStored);
        Assert.True(s_validator.TryValidate(Json(json => json["originalStored"] = true), out manifest, out error), error);
        Assert.True(manifest.IsOriginalStored);
        Assert.True(s_validator.TryValidate(Json(json => SetUrl(json, "https://photos.example.com/p/1")), out manifest, out error), error);
        Assert.Equal("https://photos.example.com/p/1", manifest.Sources[0].Url);
    }

    [Theory]
    [InlineData("\"no\"")]
    [InlineData("0")]
    [InlineData("null")]
    public void The_schema_rejects_an_original_stored_that_is_not_a_boolean(string value)
    {
        Assert.False(s_validator.TryValidate(Json(json => json["originalStored"] = JsonNode.Parse(value)), out _, out var error));
        Assert.Contains("/originalStored", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://photos.example.com/p/1")]
    [InlineData("ftp://photos.example.com/p/1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//photos.example.com/p/1")]
    [InlineData("HTTPS://photos.example.com/p/1")]
    [InlineData("")]
    public void The_schema_rejects_a_source_url_that_is_not_https(string url)
    {
        Assert.False(s_validator.TryValidate(Json(json => SetUrl(json, url)), out _, out var error));
        Assert.Contains("/sources/0/url", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_schema_limits_a_source_url_to_2000_characters()
    {
        var longest = "https://example.com/" + new string('a', ManifestSource.MaxUrlLength - "https://example.com/".Length);
        Assert.Equal(ManifestSource.MaxUrlLength, longest.Length);

        Assert.True(s_validator.TryValidate(Json(json => SetUrl(json, longest)), out _, out var error), error);
        Assert.False(s_validator.TryValidate(Json(json => SetUrl(json, longest + "a")), out _, out error));
        Assert.Contains("/sources/0/url", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void The_original_is_stored_when_either_side_stores_it_whatever_the_order(bool firstStored, bool secondStored, bool expected)
    {
        var first = Photo(Source("HD")) with { OriginalStored = firstStored ? null : false };
        var second = Photo(Source("Takeout")) with { OriginalStored = secondStored ? true : false };

        var forward = ManifestMerge.Merge(first, second);
        var backward = ManifestMerge.Merge(second, first);

        Assert.Equal(expected, forward.IsOriginalStored);
        Assert.Equal(expected, backward.IsOriginalStored);
        Assert.Equal(ManifestJson.Serialize(forward), ManifestJson.Serialize(backward));
    }

    [Fact]
    public void A_stored_original_is_written_as_absent_so_an_unchanged_manifest_is_not_rewritten()
    {
        var explicitTrue = Photo(Source("HD")) with { OriginalStored = true };
        var absent = Photo(Source("HD"));

        Assert.Equal(
            ManifestJson.Serialize(ManifestMerge.Canonical(absent)),
            ManifestJson.Serialize(ManifestMerge.Canonical(explicitTrue)));
        Assert.Equal(
            ManifestJson.Serialize(ManifestMerge.Merge(absent, absent)),
            ManifestJson.Serialize(ManifestMerge.Merge(absent, explicitTrue)));
    }

    [Fact]
    public void An_index_only_manifest_merged_with_itself_stays_index_only()
    {
        var manifest = Photo(Source("Takeout")) with { OriginalStored = false };

        Assert.False(ManifestMerge.Merge(manifest, manifest).IsOriginalStored);
        Assert.False(ManifestMerge.Canonical(manifest).IsOriginalStored);
    }

    [Fact]
    public void A_source_url_follows_the_newer_modified_time()
    {
        var older = Photo(Source("Takeout", hour: 10) with { Url = "https://photos.example.com/old" });
        var newer = Photo(Source("Takeout", hour: 12) with { Url = "https://photos.example.com/new" });

        foreach (var merged in new[] { ManifestMerge.Merge(older, newer), ManifestMerge.Merge(newer, older) })
        {
            var source = Assert.Single(merged.Sources);
            Assert.Equal("https://photos.example.com/new", source.Url);
            Assert.Equal(12, source.ModifiedAt.Hour);
        }
    }

    [Fact]
    public void A_present_url_beats_an_absent_one_even_on_the_older_side()
    {
        var withUrl = Photo(Source("Takeout", hour: 10) with { Url = "https://photos.example.com/p" });
        var without = Photo(Source("Takeout", hour: 12));

        foreach (var merged in new[] { ManifestMerge.Merge(withUrl, without), ManifestMerge.Merge(without, withUrl) })
        {
            var source = Assert.Single(merged.Sources);
            Assert.Equal("https://photos.example.com/p", source.Url);
            Assert.Equal(12, source.ModifiedAt.Hour);
        }
    }

    [Fact]
    public void Two_urls_with_the_same_modified_time_merge_to_the_same_one_in_both_orders()
    {
        var a = Photo(Source("Takeout") with { Url = "https://photos.example.com/a" });
        var b = Photo(Source("Takeout") with { Url = "https://photos.example.com/b" });

        var forward = ManifestMerge.Merge(a, b);
        var backward = ManifestMerge.Merge(b, a);

        Assert.Equal(ManifestJson.Serialize(forward), ManifestJson.Serialize(backward));
        Assert.Equal("https://photos.example.com/b", Assert.Single(forward.Sources).Url);
    }

    [Fact]
    public void Sources_of_other_names_keep_their_own_urls()
    {
        var takeout = Photo(Source("Takeout") with { Url = "https://photos.example.com/p" }) with { OriginalStored = false };
        var drive = Photo(Source("HD da Ana"));

        var merged = ManifestMerge.Merge(takeout, drive);

        Assert.True(merged.IsOriginalStored);
        Assert.Equal(["HD da Ana:", "Takeout:https://photos.example.com/p"], merged.Sources.Select(s => $"{s.Name}:{s.Url}"));
        Assert.Equal(ManifestJson.Serialize(merged), ManifestJson.Serialize(ManifestMerge.Merge(drive, takeout)));
    }

    [Fact]
    public void Embeddings_and_faces_read_the_largest_preview_the_manifest_lists()
    {
        Assert.Equal("preview-2048", ManifestEmbeddings.SourceDerivative(Photo(Source("HD")) with
        {
            Derivatives = [Derivative("thumb-256"), Derivative("preview-1280"), Derivative("preview-2048")],
        })!.Name);
        Assert.Equal("preview-1280", ManifestEmbeddings.SourceDerivative(Photo(Source("HD")))!.Name);
        Assert.Equal("poster", ManifestEmbeddings.SourceDerivative(Photo(Source("HD")) with
        {
            Kind = MediaKind.Video,
            Derivatives = [Derivative("poster"), Derivative("thumb-256"), Derivative("proxy-1080p")],
        })!.Name);
        Assert.Null(ManifestEmbeddings.SourceDerivative(Photo(Source("HD")) with { Derivatives = [Derivative("thumb-256")] }));
        Assert.Null(ManifestEmbeddings.SourceDerivative(Photo(Source("HD")) with { Derivatives = [] }));
    }

    private const string Id = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";

    private static ManifestSource Source(string name, int hour = 12) =>
        new("local", name, "a.jpg", Noon.AddHours(hour - 12));

    private static ManifestDerivative Derivative(string name) =>
        new(name, $"derivatives/{Id}/{name}.webp", 256, 192, "image/webp");

    private static Manifest Photo(ManifestSource source) => new()
    {
        Id = Id,
        Kind = MediaKind.Photo,
        SizeBytes = 1000,
        ContentType = "image/jpeg",
        Width = 4032,
        Height = 3024,
        Sources = [source],
        Derivatives = [Derivative("thumb-256"), Derivative("preview-1280")],
    };

    private static string Json(Action<JsonObject>? change = null)
    {
        var node = JsonNode.Parse(ManifestJson.Serialize(Photo(Source("HD"))))!.AsObject();
        change?.Invoke(node);
        return node.ToJsonString();
    }

    private static void SetUrl(JsonObject json, string url) => json["sources"]![0]!.AsObject()["url"] = url;
}
