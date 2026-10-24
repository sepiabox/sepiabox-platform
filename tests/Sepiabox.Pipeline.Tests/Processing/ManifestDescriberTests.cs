using Sepiabox.Pipeline.Exports;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Processing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Pipeline.Tests.Processing;

/// <summary>How a file's metadata, path and export become a manifest (S-065, D-118).</summary>
public sealed class ManifestDescriberTests
{
    private static readonly MediaMetadata s_porto = new(
        new DateTime(2019, 7, 14, 16, 30, 5), new GeoPoint(41.18, -8.69), new CameraInfo("Apple", "iPhone 8"), 4032, 3024, null);

    private static ManifestDescriber.Request Request(MediaMetadata metadata, string path = "Fotos/2019/porto.jpg", bool indexOnly = false, ExportMetadata? export = null, TimeZoneInfo? zone = null) =>
        new(PhotoId, MediaKind.Photo, 2_345_678, path, metadata, Source("HD da Ana", "Fotos/2019/porto.jpg"), indexOnly, TimeProvider.System, export, zone);

    [Fact]
    public void Maps_what_the_file_says_about_itself()
    {
        var manifest = ManifestDescriber.Describe(Request(s_porto));

        Assert.Equal((PhotoId, MediaKind.Photo, 2_345_678L, "image/jpeg"), (manifest.Id, manifest.Kind, manifest.SizeBytes, manifest.ContentType));
        Assert.Equal(new CaptureDate(new DateTime(2019, 7, 14, 16, 30, 5), CaptureDateSource.Metadata), manifest.Captured);
        Assert.Equal(new GeoPoint(41.18, -8.69), manifest.Location);
        Assert.Equal(new CameraInfo("Apple", "iPhone 8"), manifest.Camera);
        Assert.Equal((4032, 3024), (manifest.Width, manifest.Height));
        Assert.Equal("HD da Ana", Assert.Single(manifest.Sources).Name);
        Assert.Empty(manifest.Derivatives);
        Assert.Null(manifest.Caption);
    }

    [Fact]
    public void Writes_original_stored_only_for_index_only()
    {
        Assert.Null(ManifestDescriber.Describe(Request(s_porto)).OriginalStored);
        Assert.False(ManifestDescriber.Describe(Request(s_porto, indexOnly: true)).OriginalStored);
    }

    [Fact]
    public void Takes_the_date_from_the_path_when_the_file_has_none()
    {
        var manifest = ManifestDescriber.Describe(Request(MediaMetadata.Empty with { Width = 10, Height = 10 }, path: "Viagens/2012-08-03/IMG_0001.jpg"));

        Assert.Equal(CaptureDateSource.Folder, manifest.Captured!.Source);
        Assert.Equal(new DateTime(2012, 8, 3), manifest.Captured.Value.Date);
    }

    [Fact]
    public void Takes_an_exports_date_caption_albums_and_place_when_the_file_has_none_and_leaves_the_files_own_place()
    {
        var export = new ExportMetadata
        {
            CapturedAt = new DateTime(2019, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            Location = new GeoPoint(38.7, -9.1),
            Caption = "Praia",
            Albums = ["Verão", "Ferias"],
        };
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(1), "test", "test");

        var undated = ManifestDescriber.Describe(Request(MediaMetadata.Empty, path: "IMG_0001.jpg", export: export, zone: zone));
        var located = ManifestDescriber.Describe(Request(s_porto, export: export, zone: zone));

        Assert.Equal(new CaptureDate(new DateTime(2019, 7, 1, 11, 0, 0), CaptureDateSource.Export), undated.Captured);
        Assert.Equal(new GeoPoint(38.7, -9.1), undated.Location);
        Assert.Equal("Praia", undated.Caption);
        Assert.Equal(["Ferias", "Verão"], undated.Albums);
        // The file's own date and place win over the export's.
        Assert.Equal(CaptureDateSource.Metadata, located.Captured!.Source);
        Assert.Equal(new GeoPoint(41.18, -8.69), located.Location);
    }
}
