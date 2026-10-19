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

    public static Manifest Photo(ManifestSource source) => new()
    {
        Id = PhotoId,
        Kind = MediaKind.Photo,
        SizeBytes = 2_345_678,
        ContentType = "image/jpeg",
        CapturedAt = new DateTime(2019, 7, 14, 16, 30, 0),
        Location = new GeoPoint(41.1579, -8.6291),
        Camera = new CameraInfo("Apple", "iPhone 8"),
        Width = 4032,
        Height = 3024,
        Sources = [source],
        Derivatives = [Derivative("thumb-256", 256, 192), Derivative("preview-1280", 1280, 960)],
    };
}
