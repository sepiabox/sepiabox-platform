using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Processing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Pipeline.Tests.Processing;

/// <summary>The optional steps after a file's derivatives (S-065, D-118): which run, in what order, and how the manifest is threaded.</summary>
public sealed class ManifestStepsTests
{
    private static readonly FakeEmbedder s_embedder = new();
    private static readonly FakeFaceDetector s_faces = new();

    private static List<ManifestStep> Run(Manifest manifest, bool embedder, bool faces)
    {
        var seen = new List<ManifestStep>();
        ManifestSteps.Add(manifest, "work", embedder ? s_embedder : null, faces ? s_faces : null, TimeProvider.System, (step, _) =>
        {
            seen.Add(step);
            return null;
        });
        return seen;
    }

    private static Manifest OfKind(MediaKind kind) => Photo(Source("HD da Ana", "a.jpg")) with { Kind = kind };

    [Fact]
    public void Runs_the_embedding_then_the_sharpness_then_the_faces_for_a_photo()
    {
        Assert.Equal([ManifestStep.Embedding, ManifestStep.Quality, ManifestStep.Faces], Run(OfKind(MediaKind.Photo), embedder: true, faces: true));
    }

    [Fact]
    public void Still_measures_a_photos_sharpness_without_an_embedder_and_without_faces()
    {
        Assert.Equal([ManifestStep.Quality, ManifestStep.Faces], Run(OfKind(MediaKind.Photo), embedder: false, faces: true));
        Assert.Equal([ManifestStep.Embedding, ManifestStep.Quality], Run(OfKind(MediaKind.Photo), embedder: true, faces: false));
        Assert.Equal([ManifestStep.Quality], Run(OfKind(MediaKind.Photo), embedder: false, faces: false));
    }

    [Fact]
    public void Does_not_measure_the_sharpness_of_a_video()
    {
        Assert.Equal([ManifestStep.Embedding, ManifestStep.Faces], Run(OfKind(MediaKind.Video), embedder: true, faces: true));
        Assert.Empty(Run(OfKind(MediaKind.Video), embedder: false, faces: false));
    }

    [Fact]
    public void Threads_each_steps_manifest_into_the_next_and_keeps_it_when_a_step_gives_none()
    {
        var starting = OfKind(MediaKind.Photo);
        var seenByFaces = default(string?);

        var result = ManifestSteps.Add(starting, "work", s_embedder, s_faces, TimeProvider.System, (step, add) =>
        {
            switch (step)
            {
                case ManifestStep.Embedding:
                    return new EmbeddingOutcome(EmbeddingStatus.Added, starting with { Caption = "after the embedding" });
                case ManifestStep.Quality:
                    // A step with no outcome leaves the manifest as it was.
                    return null;
                default:
                    seenByFaces = "ran";
                    return new EmbeddingOutcome(EmbeddingStatus.Added, starting with { Caption = "after the faces" });
            }
        });

        Assert.Equal("ran", seenByFaces);
        Assert.Equal("after the faces", result.Caption);
    }

    [Fact]
    public void Gives_the_runner_a_step_that_adds_to_the_manifest_so_far()
    {
        var manifest = OfKind(MediaKind.Photo);
        Func<EmbeddingOutcome?>? embedding = null;

        ManifestSteps.Add(manifest, "work", s_embedder, null, TimeProvider.System, (step, add) =>
        {
            if (step == ManifestStep.Embedding)
            {
                embedding = add;
            }

            return null;
        });

        // The step is lazy: nothing is read from the work folder until the caller runs it.
        Assert.NotNull(embedding);
        Assert.DoesNotContain(s_embedder.Embedded, p => p.Contains("work", StringComparison.Ordinal));
    }
}
