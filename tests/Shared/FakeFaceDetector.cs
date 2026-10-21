using Sepiabox.Pipeline.Faces;

namespace Sepiabox.Tests.Shared;

/// <summary>
/// A face detector that needs no model: it finds <see cref="Faces"/> in every image, or throws what
/// <see cref="Failure"/> makes, so tests can drive how processing reacts to faces that are found or fail.
/// </summary>
internal sealed class FakeFaceDetector : IFaceDetector
{
    /// <summary>One face at the centre of the image, with a unit embedding.</summary>
    public static DetectedFace CentreFace(double score = 0.97)
    {
        var embedding = new float[FaceDetector.Dimensions];
        embedding[0] = 1f;
        return new DetectedFace(0.25, 0.2, 0.5, 0.6, score, embedding);
    }

    public IReadOnlyList<DetectedFace> Faces { get; set; } = [CentreFace()];

    /// <summary>When set, <see cref="Detect"/> throws what it returns for the path.</summary>
    public Func<string, Exception?>? Failure { get; set; }

    public List<string> Detected { get; } = [];

    public IReadOnlyList<DetectedFace> Detect(string path)
    {
        if (Failure?.Invoke(path) is { } failure)
        {
            throw failure;
        }

        // process runs files in parallel (D-078).
        lock (Detected)
        {
            Detected.Add(path);
        }

        return Faces;
    }
}
