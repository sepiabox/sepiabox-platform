using ImageMagick;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Tests;

/// <summary><c>process</c> with the real YuNet and SFace models on a public-domain portrait (S-018).</summary>
[Trait("Category", "Integration")]
public sealed class FaceProcessingTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [FacesFact]
    public void Process_with_faces_stores_the_face_of_the_portrait()
    {
        _drive.Write("2019/retrato.jpg", File.ReadAllBytes(Path.Combine("TestData", "face-portrait.jpg")));

        var result = ProcessCommand.Run(Options(faces: true));

        Assert.Equal((1, 1, 1), (result.Processed, result.FacesChecked, result.FacesFound));
        Assert.Empty(result.FaceProblems);
        var manifest = Assert.Single(ReadManifests());
        Assert.True(manifest.FacesProcessed);
        var face = Assert.Single(manifest.Faces);
        Assert.InRange(face.Score, FaceDetector.MinimumScore, 1);
        Assert.True(face.Box[0] + face.Box[2] <= 1 && face.Box[1] + face.Box[3] <= 1, $"box {string.Join(", ", face.Box)}");
        Assert.Equal(FaceDetector.Dimensions, EmbeddingCodec.Decode(face.Embedding).Length);
        Assert.True(new ManifestValidator().TryValidate(File.ReadAllText(ManifestFile()), out _, out var problem), problem);
    }

    [FacesFact]
    public void Process_without_faces_leaves_faces_unprocessed_even_with_the_models_downloaded()
    {
        _drive.Write("2019/retrato.jpg", File.ReadAllBytes(Path.Combine("TestData", "face-portrait.jpg")));

        var result = ProcessCommand.Run(Options(faces: false));

        Assert.Equal((1, 0), (result.Processed, result.FacesChecked));
        var manifest = Assert.Single(ReadManifests());
        Assert.False(manifest.FacesProcessed);
        Assert.Empty(manifest.Faces);
    }

    [FacesFact]
    public void Finds_the_face_of_a_portrait_stored_sideways_with_an_exif_orientation()
    {
        // Stored rotated a quarter turn anticlockwise, with Orientation 6 (rotate clockwise to display), as phones do.
        using (var image = new MagickImage(Path.Combine("TestData", "face-portrait.jpg")))
        {
            image.Rotate(-90);
            // Both, as in TestPhotos.Sized: the attribute alone writes no EXIF profile, and ImageMagick writes the
            // profile's tag from the attribute. The check below proves the saved file says 6.
            image.Orientation = Orientation.RightTop;
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            _drive.Write("2019/deitado.jpg", image.ToByteArray(MagickFormat.Jpeg));
        }

        using (var stored = new MagickImage(Path.Combine(_drive.Path, "2019", "deitado.jpg")))
        {
            Assert.Equal(Orientation.RightTop, stored.Orientation);
        }

        ProcessCommand.Run(Options(faces: true));

        var face = Assert.Single(Assert.Single(ReadManifests()).Faces);
        // Upright: the face is in the upper middle of the preview, as in the portrait itself.
        Assert.InRange(face.Box[0] + (face.Box[2] / 2), 0.3, 0.5);
        Assert.InRange(face.Box[1] + (face.Box[3] / 2), 0.1, 0.4);
    }

    private ProcessOptions Options(bool faces) =>
        new(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.Folder, Faces: faces);

    private string ManifestFile() => Assert.Single(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json"));

    private List<Manifest> ReadManifests() => [ManifestJson.Deserialize(File.ReadAllText(ManifestFile()))];
}
