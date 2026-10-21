using Sepiabox.Pipeline.Manifests;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Pipeline.Tests;

/// <summary>Turning face recognition off erases faces from stored manifests through the merge (S-022, D-081).</summary>
public sealed class FacesErasureMergeTests
{
    private static readonly DateTimeOffset Before = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ErasedAt = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset After = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Erases_the_stored_faces_and_keeps_everything_else()
    {
        var stored = ManifestMerge.Canonical(WithFaces("HD da Ana", Before));

        var erased = ManifestMerge.EraseFaces(stored, ErasedAt);

        Assert.False(erased.FacesProcessed);
        Assert.Empty(erased.Faces);
        Assert.Null(erased.FacesVersion);
        Assert.Null(erased.FacesDetectedAt);
        Assert.Equal(ErasedAt, erased.FacesErasedAt);
        var others = stored with { FacesProcessed = false, Faces = [], FacesVersion = null, FacesDetectedAt = null, FacesErasedAt = ErasedAt };
        Assert.Equal(ManifestJson.Serialize(others), ManifestJson.Serialize(erased));
    }

    [Fact]
    public void Never_lets_faces_found_before_the_erase_come_back_in_either_order()
    {
        var erased = ManifestMerge.EraseFaces(WithFaces("HD da Ana", Before), ErasedAt);
        // A work folder processed with --faces before the erase, uploaded again; and one from before facesDetectedAt existed.
        foreach (var old in new[] { WithFaces("HD do Jonathas", Before), WithFaces("HD antigo", null) })
        {
            foreach (var merged in new[] { ManifestMerge.Merge(erased, old), ManifestMerge.Merge(old, erased) })
            {
                Assert.False(merged.FacesProcessed);
                Assert.Empty(merged.Faces);
                Assert.Equal(ErasedAt, merged.FacesErasedAt);
                Assert.Contains(merged.Sources, s => s.Name == old.Sources[0].Name);
            }
        }
    }

    [Fact]
    public void Keeps_faces_found_after_the_erase_in_either_order()
    {
        var erased = ManifestMerge.EraseFaces(WithFaces("HD da Ana", Before), ErasedAt);
        var fresh = WithFaces("HD do Jonathas", After, axis: 3);

        foreach (var merged in new[] { ManifestMerge.Merge(erased, fresh), ManifestMerge.Merge(fresh, erased) })
        {
            Assert.True(merged.FacesProcessed);
            Assert.Equal(fresh.Faces, merged.Faces);
            Assert.Equal(After, merged.FacesDetectedAt);
            Assert.Equal(ErasedAt, merged.FacesErasedAt);
        }
    }

    [Fact]
    public void Lets_newer_faces_win_over_an_older_pipeline_s_faces_found_after_the_erase_only_if_they_survive()
    {
        // A newer face pipeline found before the erase loses to version 1 found after it: erased faces never compete.
        var erased = ManifestMerge.EraseFaces(WithFaces("HD da Ana", Before), ErasedAt);
        var newerButOld = WithFaces("HD antigo", Before, axis: 5) with { FacesVersion = 2 };
        var fresh = WithFaces("HD do Jonathas", After, axis: 3);

        var merged = ManifestMerge.Merge(ManifestMerge.Merge(erased, newerButOld), fresh);

        Assert.Equal(fresh.Faces, merged.Faces);
        Assert.Equal(1, merged.EffectiveFacesVersion);
    }

    [Fact]
    public void Settles_on_the_faces_found_after_the_erase_whatever_order_the_uploads_reach_an_erased_blob()
    {
        // Uploads are merged into the stored blob one at a time (D-036): once the blob carries the erase, the order of
        // the uploads that follow does not matter.
        var erased = ManifestMerge.EraseFaces(WithFaces("HD da Ana", Before), ErasedAt);
        Manifest[] uploads =
        [
            WithFaces("HD antigo", Before, axis: 5) with { FacesVersion = 2 },
            WithFaces("HD do Jonathas", After, axis: 3),
            WithFaces("Pen", null, axis: 4),
            Photo(Source("Telemóvel", "b.jpg")),
        ];

        var results = Permutations(uploads)
            .Select(order => order.Aggregate(erased, ManifestMerge.Merge))
            .Select(merged => ManifestJson.Serialize(merged with { Sources = [] }))
            .Distinct()
            .ToList();

        var result = ManifestJson.Deserialize(Assert.Single(results));
        Assert.Equal(uploads[1].Faces, result.Faces);
        Assert.Equal(After, result.FacesDetectedAt);
    }

    [Fact]
    public void Keeps_the_newer_erase_in_either_order()
    {
        var first = ManifestMerge.EraseFaces(WithFaces("HD da Ana", Before), ErasedAt);
        var second = ManifestMerge.EraseFaces(first, After);

        Assert.Equal(After, ManifestMerge.Merge(first, second).FacesErasedAt);
        Assert.Equal(After, ManifestMerge.Merge(second, first).FacesErasedAt);
        // An older erase merged into a newer one changes nothing.
        Assert.Equal(ManifestJson.Serialize(second), ManifestJson.Serialize(ManifestMerge.EraseFaces(second, ErasedAt)));
    }

    [Fact]
    public void Erases_faces_said_to_be_found_after_their_blob_was_written()
    {
        // The computer's clock was a day ahead when it ran process: the time is after the erase, but cannot be trusted.
        var stored = WithFaces("HD da Ana", ErasedAt.AddDays(1));

        Assert.Empty(ManifestMerge.EraseFaces(stored, ErasedAt, writtenAt: ErasedAt.AddMinutes(-30)).Faces);
        Assert.Single(ManifestMerge.EraseFaces(stored, ErasedAt, writtenAt: ErasedAt.AddDays(1).AddMinutes(-4)).Faces);
        Assert.Single(ManifestMerge.EraseFaces(stored, ErasedAt).Faces);
    }

    [Theory]
    [InlineData(-60, true)]
    [InlineData(0, true)]
    [InlineData(5 * 60, true)]
    [InlineData((6 * 60) - 1, true)]
    [InlineData(6 * 60, true)]
    [InlineData(7 * 60, false)]
    [InlineData(24 * 60 * 60, false)]
    public void Trusts_a_detection_time_up_to_six_minutes_after_the_blob_was_written(int secondsAfter, bool trusted)
    {
        var written = ErasedAt;
        var manifest = WithFaces("HD da Ana", written.AddSeconds(secondsAfter));

        Assert.Equal(trusted, ManifestMerge.DetectionTrusted(manifest, written));
        Assert.True(ManifestMerge.DetectionTolerance > TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Erases_faces_found_at_the_very_moment_of_the_erase()
    {
        var stored = WithFaces("HD da Ana", ErasedAt);

        Assert.Empty(ManifestMerge.EraseFaces(stored, ErasedAt).Faces);
    }

    [Fact]
    public void Merges_the_same_faces_found_at_two_times_to_the_later_time_in_either_order()
    {
        var early = WithFaces("HD da Ana", After);
        var late = WithFaces("HD do Jonathas", After.AddHours(1));

        Assert.Equal(After.AddHours(1), ManifestMerge.Merge(early, late).FacesDetectedAt);
        Assert.Equal(After.AddHours(1), ManifestMerge.Merge(late, early).FacesDetectedAt);
    }

    [Fact]
    public void Reads_and_writes_the_detection_and_erase_times()
    {
        var manifest = ManifestMerge.EraseFaces(WithFaces("HD da Ana", Before), ErasedAt);
        var withFresh = ManifestMerge.Merge(manifest, WithFaces("HD do Jonathas", After));

        var json = ManifestJson.Serialize(withFresh);
        var validator = new ManifestValidator();

        Assert.Contains("\"facesDetectedAt\": \"2026-10-22T09:00:00+00:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"facesErasedAt\": \"2026-10-21T09:00:00+00:00\"", json, StringComparison.Ordinal);
        Assert.True(validator.TryValidate(json, out var read, out var problem), problem);
        Assert.Equal(After, read.FacesDetectedAt);
        Assert.Equal(ErasedAt, read.FacesErasedAt);
    }

    [Fact]
    public void Refuses_a_detection_time_without_faces_processed()
    {
        var json = ManifestJson.Serialize(Photo(Source("HD da Ana", "a.jpg")) with { FacesDetectedAt = After });

        Assert.False(new ManifestValidator().TryValidate(json, out _, out _));
    }

    private static IEnumerable<Manifest[]> Permutations(Manifest[] items) =>
        items.Length <= 1
            ? [items]
            : items.SelectMany((first, i) => Permutations([.. items[..i], .. items[(i + 1)..]]).Select(rest => (Manifest[])[first, .. rest]));

    private static Manifest WithFaces(string source, DateTimeOffset? detectedAt, int axis = 0) =>
        Photo(Source(source, "a.jpg")) with { FacesProcessed = true, FacesVersion = 1, Faces = [Face(0.9, axis)], FacesDetectedAt = detectedAt };
}
