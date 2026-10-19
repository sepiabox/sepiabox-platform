using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public sealed class ManifestMergeTests
{
    private const string Id = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";

    [Fact]
    public void Keeps_every_source_and_takes_the_newer_time_for_the_same_file()
    {
        var existing = Photo(Source("HD da Ana", "a.jpg", hour: 10), Source("HD da Ana", "b.jpg", hour: 10));
        var incoming = Photo(Source("HD da Ana", "a.jpg", hour: 12), Source("HD do Jonathas", "c.jpg", hour: 9));

        var merged = ManifestMerge.Merge(existing, incoming);

        Assert.Equal(
            ["HD da Ana/a.jpg@12", "HD da Ana/b.jpg@10", "HD do Jonathas/c.jpg@9"],
            merged.Sources.Select(s => $"{s.Name}/{s.Path}@{s.ModifiedAt.Hour}"));
    }

    [Fact]
    public void An_older_copy_of_the_same_file_does_not_go_back_in_time()
    {
        var merged = ManifestMerge.Merge(
            Photo(Source("HD da Ana", "a.jpg", hour: 12)),
            Photo(Source("HD da Ana", "a.jpg", hour: 10)));

        Assert.Equal(12, Assert.Single(merged.Sources).ModifiedAt.Hour);
    }

    [Fact]
    public void Merges_embeddings_by_model_key_with_the_incoming_value_winning()
    {
        var existing = Photo(Source("HD da Ana", "a.jpg")) with
        {
            Embeddings = new Dictionary<string, JsonNode> { ["clip-vit-b32"] = "old", ["other-model"] = "kept" },
        };
        var incoming = Photo(Source("HD do Jonathas", "c.jpg")) with
        {
            Embeddings = new Dictionary<string, JsonNode> { ["clip-vit-b32"] = "new" },
        };

        var merged = ManifestMerge.Merge(existing, incoming);

        Assert.Equal(
            ["clip-vit-b32=new", "other-model=kept"],
            merged.Embeddings.Select(e => $"{e.Key}={e.Value.GetValue<string>()}"));
    }

    [Fact]
    public void A_manifest_without_embeddings_never_drops_the_stored_ones()
    {
        var existing = Photo(Source("HD da Ana", "a.jpg")) with
        {
            Embeddings = new Dictionary<string, JsonNode> { ["clip-vit-b32"] = "stored" },
        };

        var merged = ManifestMerge.Merge(existing, Photo(Source("HD do Jonathas", "c.jpg")));

        Assert.Equal("stored", merged.Embeddings["clip-vit-b32"].GetValue<string>());
    }

    [Fact]
    public void Keeps_every_derivative_and_takes_the_incoming_one_for_the_same_name()
    {
        var existing = Photo(Source("HD da Ana", "a.jpg")) with { Derivatives = [Derivative("thumb-256", 256), Derivative("poster", 1280)] };
        var incoming = Photo(Source("HD do Jonathas", "c.jpg")) with { Derivatives = [Derivative("thumb-256", 200), Derivative("proxy-720p", 1280)] };

        var merged = ManifestMerge.Merge(existing, incoming);

        Assert.Equal(["poster:1280", "proxy-720p:1280", "thumb-256:200"], merged.Derivatives.Select(d => $"{d.Name}:{d.Width}"));
    }

    [Fact]
    public void A_derivative_listed_twice_does_not_stop_the_merge()
    {
        var existing = Photo(Source("HD da Ana", "a.jpg")) with { Derivatives = [Derivative("thumb-256", 256), Derivative("thumb-256", 200)] };

        var merged = ManifestMerge.Merge(existing, Photo(Source("HD da Ana", "a.jpg")) with { Derivatives = [] });

        Assert.Equal(200, Assert.Single(merged.Derivatives).Width);
    }

    [Fact]
    public void The_same_file_written_with_windows_and_linux_separators_is_one_source()
    {
        var merged = ManifestMerge.Merge(
            Photo(Source("HD da Ana", "2019\\praia.jpg", hour: 10)),
            Photo(Source("HD da Ana", "2019/praia.jpg", hour: 12)));

        Assert.Equal("2019/praia.jpg", Assert.Single(merged.Sources).Path);
    }

    [Theory]
    [InlineData("2019\\praia.jpg", "2019/praia.jpg", true)]
    [InlineData("2019/praia.jpg", "2019/praia.jpg", true)]
    [InlineData("2019/Praia.jpg", "2019/praia.jpg", false)]
    public void Source_identity_ignores_the_path_separator_but_not_case(string first, string second, bool same)
    {
        Assert.Equal(same, Source("HD da Ana", first).IsSameFile(Source("HD da Ana", second)));
    }

    [Fact]
    public void Incoming_description_wins_when_it_is_complete()
    {
        var existing = Photo(Source("HD da Ana", "a.jpg")) with { Width = 100 };
        var incoming = Photo(Source("HD do Jonathas", "c.jpg")) with { Width = 4032 };

        Assert.Equal(4032, ManifestMerge.Merge(existing, incoming).Width);
    }

    [Fact]
    public void A_pending_video_does_not_erase_what_ffprobe_found_elsewhere()
    {
        var done = Video(Source("HD da Ana", "v.mp4")) with
        {
            Width = 1920,
            Height = 1080,
            DurationSeconds = 3,
            Derivatives = [Derivative("poster", 1280), Derivative("proxy-720p", 1280)],
        };
        var pending = Video(Source("HD do Jonathas", "v.mp4")) with { Width = null, Height = null, DurationSeconds = null, Derivatives = [] };

        var merged = ManifestMerge.Merge(done, pending);

        Assert.Equal((1920, 1080, 3.0), (merged.Width, merged.Height, merged.DurationSeconds));
        Assert.Equal(2, merged.Derivatives.Count);
        Assert.Equal(2, merged.Sources.Count);
    }

    [Fact]
    public void A_photo_without_thumbnails_does_not_replace_a_described_one()
    {
        var described = Photo(Source("HD da Ana", "a.jpg"));
        var failed = Photo(Source("HD do Jonathas", "a.jpg")) with { Width = null, Derivatives = [] };

        var merged = ManifestMerge.Merge(described, failed);

        Assert.Equal(4032, merged.Width);
        Assert.Equal(2, merged.Derivatives.Count);
    }

    [Fact]
    public void Refuses_to_merge_different_assets()
    {
        var other = Photo(Source("HD da Ana", "a.jpg")) with { Id = new string('f', 64) };

        Assert.Throws<ArgumentException>(() => ManifestMerge.Merge(Photo(Source("HD da Ana", "a.jpg")), other));
    }

    private static ManifestSource Source(string name, string path, int hour = 17) =>
        new("local", name, path, new DateTimeOffset(2019, 7, 14, hour, 0, 0, TimeSpan.Zero));

    private static ManifestDerivative Derivative(string name, int width) =>
        new(name, $"derivatives/{Id}/{name}.webp", width, width * 3 / 4, "image/webp");

    private static Manifest Photo(params ManifestSource[] sources) => new()
    {
        Id = Id,
        Kind = MediaKind.Photo,
        SizeBytes = 1000,
        ContentType = "image/jpeg",
        Width = 4032,
        Height = 3024,
        Sources = sources,
        Derivatives = [Derivative("thumb-256", 256), Derivative("preview-1280", 1280)],
    };

    private static Manifest Video(ManifestSource source) => new()
    {
        Id = Id,
        Kind = MediaKind.Video,
        SizeBytes = 1000,
        ContentType = "video/mp4",
        Sources = [source],
    };
}
