using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Tests.Shared;

internal static class TestManifests
{
    public const string PhotoId = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";

    public static ManifestSource Source(string name, string path, int hour = 17) =>
        new("local", name, path, new DateTimeOffset(2019, 7, 14, hour, 0, 0, TimeSpan.FromHours(1)));

    public static ManifestDerivative Derivative(string name, int width, int height) =>
        new(name, $"derivatives/{PhotoId}/{name}.webp", width, height, "image/webp");

    /// <summary>A face with a valid 128-value unit embedding along <paramref name="axis"/>, so faces can differ.</summary>
    public static ManifestFace Face(double score, int axis = 0)
    {
        var embedding = new float[128];
        embedding[axis] = 1f;
        return new ManifestFace([0.1, 0.2, 0.3, 0.4], score, Sepiabox.Pipeline.Embeddings.EmbeddingCodec.Encode(embedding));
    }

    public static Manifest Photo(ManifestSource source) => new()
    {
        Id = PhotoId,
        Kind = MediaKind.Photo,
        SizeBytes = 2_345_678,
        ContentType = "image/jpeg",
        Captured = new(new DateTime(2019, 7, 14, 16, 30, 0), CaptureDateSource.Metadata),
        Location = new GeoPoint(41.1579, -8.6291),
        Camera = new CameraInfo("Apple", "iPhone 8"),
        Width = 4032,
        Height = 3024,
        Sources = [source],
        Derivatives = [Derivative("thumb-256", 256, 192), Derivative("preview-1280", 1280, 960)],
    };
}
