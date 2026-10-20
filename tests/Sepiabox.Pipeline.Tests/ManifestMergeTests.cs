using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public sealed class ManifestMergeTests
{
    private const string Id = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";
    private static readonly DateTime Taken = new(2019, 7, 14, 16, 30, 5);

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
    public void Keeps_the_metadata_date_when_the_incoming_date_comes_from_a_path()
    {
        var existing = Photo(Source("HD da Ana", "a.jpg")) with { Captured = new(Taken, CaptureDateSource.Metadata) };
        var incoming = Photo(Source("HD do Jonathas", "2005-07-14/a.jpg")) with { Captured = new(new DateTime(2005, 7, 14), CaptureDateSource.Folder) };

        var merged = ManifestMerge.Merge(existing, incoming);

        Assert.Equal((Taken, CaptureDateSource.Metadata), (merged.CapturedAt, merged.CapturedAtSource));
    }

    [Fact]
    public void Takes_the_metadata_date_when_the_stored_date_comes_from_a_path()
    {
        var existing = Photo(Source("HD da Ana", "2005-07-14/a.jpg")) with { Captured = new(new DateTime(2005, 7, 14), CaptureDateSource.Folder) };
        var incoming = Photo(Source("HD do Jonathas", "a.jpg")) with { Captured = new(Taken, CaptureDateSource.Metadata) };

        var merged = ManifestMerge.Merge(existing, incoming);

        Assert.Equal((Taken, CaptureDateSource.Metadata), (merged.CapturedAt, merged.CapturedAtSource));
    }

    [Fact]
    public void Merges_to_the_same_json_in_either_order_when_one_metadata_date_predates_the_source_field()
    {
        // Read from JSON, as an upload reads a stored manifest written before S-043.
        var old = ReadWithoutSource(Photo(Source("HD da Ana", "a.jpg")) with { Captured = new(Taken, CaptureDateSource.Metadata) });
        var incoming = Photo(Source("HD do Jonathas", "a.jpg")) with { Captured = new(Taken, CaptureDateSource.Metadata) };

        var expected = new CaptureDate(Taken, CaptureDateSource.Metadata);
        Assert.Equal(expected, old.Captured);
        Assert.Equal(expected, ManifestMerge.Merge(old, incoming).Captured);
        Assert.Equal(ManifestJson.Serialize(ManifestMerge.Merge(old, incoming)), ManifestJson.Serialize(ManifestMerge.Merge(incoming, old)));
        Assert.Equal("metadata", (string?)JsonNode.Parse(ManifestJson.Serialize(ManifestMerge.Merge(old, incoming)))!["capturedAtSource"]);
    }

    [Fact]
    public void Keeps_a_date_without_a_source_over_a_file_name_date_when_the_manifest_predates_the_field()
    {
        var old = ReadWithoutSource(Photo(Source("HD da Ana", "a.jpg")) with { Captured = new(Taken, CaptureDateSource.Metadata) });
        var incoming = Photo(Source("HD do Jonathas", "IMG_20120304_101112.jpg")) with
        {
            Captured = new(new DateTime(2012, 3, 4, 10, 11, 12), CaptureDateSource.FileName),
        };

        Assert.Equal(new CaptureDate(Taken, CaptureDateSource.Metadata), ManifestMerge.Merge(old, incoming).Captured);
        Assert.Equal(new CaptureDate(Taken, CaptureDateSource.Metadata), ManifestMerge.Merge(incoming, old).Captured);
    }

    [Fact]
    public void Takes_the_file_name_date_over_the_folder_date_in_either_order()
    {
        var folder = Video(Source("HD da Ana", "2011-09-26/clip.MTS")) with { Captured = new(new DateTime(2011, 9, 26), CaptureDateSource.Folder) };
        var fileName = Video(Source("HD do Jonathas", "2011-09-25/MNU_110425_a.MTS")) with
        {
            Captured = new(new DateTime(2011, 9, 25, 11, 4, 25), CaptureDateSource.FileName),
        };

        Assert.Equal((new DateTime(2011, 9, 25, 11, 4, 25), CaptureDateSource.FileName), DateOf(ManifestMerge.Merge(folder, fileName)));
        Assert.Equal((new DateTime(2011, 9, 25, 11, 4, 25), CaptureDateSource.FileName), DateOf(ManifestMerge.Merge(fileName, folder)));
    }

    [Fact]
    public void Takes_the_earlier_date_in_either_order_when_both_come_from_folders()
    {
        var first = Video(Source("HD da Ana", "2008-12-24/clip.mod")) with { Captured = new(new DateTime(2008, 12, 24), CaptureDateSource.Folder) };
        var second = Video(Source("HD do Jonathas", "2008-12-25/clip.mod")) with { Captured = new(new DateTime(2008, 12, 25), CaptureDateSource.Folder) };

        var merged = ManifestMerge.Merge(first, second);

        Assert.Equal((new DateTime(2008, 12, 24), CaptureDateSource.Folder), DateOf(merged));
        Assert.Equal(DateOf(merged), DateOf(ManifestMerge.Merge(second, first)));
        // Merging the result again changes nothing, so a re-upload from either drive writes nothing.
        Assert.Equal(ManifestJson.Serialize(merged), ManifestJson.Serialize(ManifestMerge.Merge(merged, second)));
        Assert.Equal(ManifestJson.Serialize(merged), ManifestJson.Serialize(ManifestMerge.Merge(merged, first)));
    }

    [Fact]
    public void Keeps_a_path_date_when_the_incoming_manifest_has_no_date()
    {
        var existing = Video(Source("HD da Ana", "2008-12-24/clip.mod")) with { Captured = new(new DateTime(2008, 12, 24), CaptureDateSource.Folder) };
        var incoming = Video(Source("HD do Jonathas", "backup/clip.mod"));

        Assert.Equal((new DateTime(2008, 12, 24), CaptureDateSource.Folder), DateOf(ManifestMerge.Merge(existing, incoming)));
        Assert.Equal((new DateTime(2008, 12, 24), CaptureDateSource.Folder), DateOf(ManifestMerge.Merge(incoming, existing)));
    }

    [Fact]
    public void Takes_the_date_of_the_complete_description_when_both_come_from_metadata()
    {
        var pending = Video(Source("HD da Ana", "v.mp4")) with { Captured = new(Taken, CaptureDateSource.Metadata) };
        var done = Video(Source("HD do Jonathas", "v.mp4")) with
        {
            Captured = new(Taken.AddHours(1), CaptureDateSource.Metadata),
            Derivatives = [Derivative("poster", 1280)],
        };

        Assert.Equal(Taken.AddHours(1), ManifestMerge.Merge(done, pending).CapturedAt);
        Assert.Equal(Taken.AddHours(1), ManifestMerge.Merge(pending, done).CapturedAt);
    }

    [Fact]
    public void A_manifest_processed_without_faces_never_erases_the_stored_faces()
    {
        var withFaces = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9)] };
        var withoutFaces = Photo(Source("HD do Jonathas", "c.jpg"));

        var merged = ManifestMerge.Merge(withFaces, withoutFaces);

        Assert.True(merged.FacesProcessed);
        Assert.Equal(0.9, Assert.Single(merged.Faces).Score);
        Assert.Equal(2, merged.Sources.Count);
    }

    [Fact]
    public void Takes_the_incoming_faces_when_the_stored_manifest_has_none_processed()
    {
        var merged = ManifestMerge.Merge(
            Photo(Source("HD da Ana", "a.jpg")),
            Photo(Source("HD do Jonathas", "c.jpg")) with { FacesProcessed = true, Faces = [Face(0.85)] });

        Assert.True(merged.FacesProcessed);
        Assert.Equal(0.85, Assert.Single(merged.Faces).Score);
    }

    [Fact]
    public void Takes_the_side_with_more_faces_in_either_order_when_both_were_processed()
    {
        var two = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9), Face(0.8, axis: 1)] };
        var none = Photo(Source("HD do Jonathas", "c.jpg")) with { FacesProcessed = true, Faces = [] };

        Assert.Equal(2, ManifestMerge.Merge(two, none).Faces.Count);
        Assert.Equal(2, ManifestMerge.Merge(none, two).Faces.Count);
    }

    [Fact]
    public void Merges_faces_to_the_same_json_in_either_order_when_both_found_as_many()
    {
        // Two computers whose previews differ slightly: re-uploading from either must not flip the stored blob.
        var ana = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.91)] };
        var jonathas = Photo(Source("HD do Jonathas", "c.jpg")) with { FacesProcessed = true, Faces = [Face(0.93, axis: 2)] };

        var one = ManifestMerge.Merge(ana, jonathas);
        var other = ManifestMerge.Merge(jonathas, ana);

        Assert.Equal(ManifestJson.Serialize(one), ManifestJson.Serialize(other));
        Assert.Equal(ManifestJson.Serialize(one), ManifestJson.Serialize(ManifestMerge.Merge(one, ana)));
        Assert.Equal(ManifestJson.Serialize(one), ManifestJson.Serialize(ManifestMerge.Merge(one, jonathas)));
    }

    [Fact]
    public void A_newer_face_pipeline_replaces_older_faces_in_either_order_even_with_fewer_faces()
    {
        var older = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9), Face(0.85, axis: 1)] };
        var newer = Photo(Source("HD do Jonathas", "c.jpg")) with { FacesProcessed = true, FacesVersion = 2, Faces = [Face(0.95, axis: 2)] };

        foreach (var merged in new[] { ManifestMerge.Merge(older, newer), ManifestMerge.Merge(newer, older) })
        {
            Assert.Equal(2, merged.FacesVersion);
            Assert.Equal(0.95, Assert.Single(merged.Faces).Score);
        }
    }

    [Fact]
    public void Faces_written_before_the_version_field_count_as_version_1()
    {
        var unversioned = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9), Face(0.85, axis: 1)] };
        var first = Photo(Source("HD do Jonathas", "c.jpg")) with { FacesProcessed = true, FacesVersion = 1, Faces = [Face(0.95)] };

        Assert.Equal(1, unversioned.EffectiveFacesVersion);
        Assert.Equal(2, ManifestMerge.Merge(first, unversioned).Faces.Count);
        Assert.Equal(2, ManifestMerge.Merge(unversioned, first).Faces.Count);
    }

    public static TheoryData<string> ThreeWays => ["tie between two computers", "a newer pipeline among them"];

    [Theory]
    [MemberData(nameof(ThreeWays))]
    public void Merges_faces_to_the_same_result_whatever_the_order_and_grouping(string which)
    {
        Manifest[] manifests = which == "tie between two computers"
            ?
            [
                Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.91)] },
                Photo(Source("HD do Jonathas", "b.jpg")) with { FacesProcessed = true, Faces = [Face(0.93, axis: 1)] },
                Photo(Source("Pen", "c.jpg")),
            ]
            :
            [
                Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.91), Face(0.8, axis: 4)] },
                Photo(Source("HD do Jonathas", "b.jpg")) with { FacesProcessed = true, Faces = [Face(0.93, axis: 1)] },
                Photo(Source("HD antigo", "c.jpg")) with { FacesProcessed = true, FacesVersion = 2, Faces = [Face(0.7, axis: 5)] },
            ];

        var results = (
            from a in manifests
            from b in manifests
            from c in manifests
            where !ReferenceEquals(a, b) && !ReferenceEquals(b, c) && !ReferenceEquals(a, c)
            from merged in new[] { ManifestMerge.Merge(ManifestMerge.Merge(a, b), c), ManifestMerge.Merge(a, ManifestMerge.Merge(b, c)) }
            select FacesOf(merged)).Distinct().ToList();

        Assert.Single(results);
    }

    [Fact]
    public void Merging_a_manifest_with_itself_changes_nothing()
    {
        var manifest = ManifestMerge.Canonical(
            Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9), Face(0.85, axis: 3)] });

        Assert.Equal(ManifestJson.Serialize(manifest), ManifestJson.Serialize(ManifestMerge.Merge(manifest, manifest)));
    }

    [Fact]
    public void Faces_read_twice_from_the_same_json_are_equal()
    {
        var json = ManifestJson.Serialize(Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9)] });

        var first = ManifestJson.Deserialize(json).Faces;
        var second = ManifestJson.Deserialize(json).Faces;

        Assert.Equal(first, second);
        Assert.Equal(first[0].GetHashCode(), second[0].GetHashCode());
        Assert.NotEqual(first[0], first[0] with { Box = [0.1, 0.2, 0.3, 0.5] });
    }

    [Fact]
    public void A_face_keeps_its_own_copy_of_the_box()
    {
        var box = new List<double> { 0.1, 0.2, 0.3, 0.4 };
        var face = new ManifestFace(box, 0.9, "AAAA");
        var changed = face with { Box = box };

        box[0] = 0.9;

        Assert.Equal([0.1, 0.2, 0.3, 0.4], face.Box);
        Assert.Equal([0.1, 0.2, 0.3, 0.4], changed.Box);
        Assert.Throws<ArgumentNullException>(() => new ManifestFace(null!, 0.9, "AAAA"));
    }

    [Fact]
    public void Keeps_faces_unprocessed_when_neither_side_looked_for_them()
    {
        var merged = ManifestMerge.Merge(Photo(Source("HD da Ana", "a.jpg")), Photo(Source("HD do Jonathas", "c.jpg")));

        Assert.False(merged.FacesProcessed);
        Assert.Empty(merged.Faces);
    }

    [Fact]
    public void Keeps_the_faces_of_a_described_photo_when_the_incoming_run_could_not_make_thumbnails()
    {
        var described = Photo(Source("HD da Ana", "a.jpg")) with { FacesProcessed = true, Faces = [Face(0.9)] };
        var failed = Photo(Source("HD do Jonathas", "a.jpg")) with { Width = null, Derivatives = [] };

        Assert.Single(ManifestMerge.Merge(described, failed).Faces);
        Assert.Single(ManifestMerge.Merge(failed, described).Faces);
    }

    [Fact]
    public void Refuses_to_merge_different_assets()
    {
        var other = Photo(Source("HD da Ana", "a.jpg")) with { Id = new string('f', 64) };

        Assert.Throws<ArgumentException>(() => ManifestMerge.Merge(Photo(Source("HD da Ana", "a.jpg")), other));
    }

    private static ManifestFace Face(double score, int axis = 0) => TestManifests.Face(score, axis);

    private static string FacesOf(Manifest manifest) =>
        $"{manifest.FacesProcessed} {manifest.FacesVersion} {System.Text.Json.JsonSerializer.Serialize(manifest.Faces, ManifestJson.Options)}";

    private static (DateTime?, CaptureDateSource?) DateOf(Manifest manifest) => (manifest.CapturedAt, manifest.CapturedAtSource);

    private static Manifest ReadWithoutSource(Manifest manifest)
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(manifest))!.AsObject();
        json.Remove("capturedAtSource");
        return ManifestJson.Deserialize(json.ToJsonString());
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
