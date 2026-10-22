using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Processing;
using Sepiabox.Pipeline.Video;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Pipeline.Tests.Processing;

/// <summary>
/// The derivatives <c>process</c> and the cloud worker share (S-065, D-118): the five ways making them can end, with a stand-in
/// for FFmpeg and tiny generated files.
/// </summary>
public sealed class DerivativeMakerTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static Manifest Undescribed(MediaKind kind) => Photo(Source("HD da Ana", "a")) with { Kind = kind, Derivatives = [] };

    private DerivativeResult Make(Manifest manifest, string path, IVideoProcessor? video = null, bool indexOnly = false, byte[]? content = null) =>
        DerivativeMaker.Make(manifest, path, content, _folder.Path, indexOnly, video, CancellationToken.None);

    [Fact]
    public void Makes_a_photos_thumbnails_and_previews()
    {
        var path = _folder.Write("a.jpg", TestPhotos.Porto());

        var result = Make(Undescribed(MediaKind.Photo), path);

        Assert.Equal(DerivativeStatus.Done, result.Status);
        Assert.Equal(ThumbnailGenerator.PhotoSizes.Select(s => s.Name), result.Manifest.Derivatives.Select(d => d.Path.Split('/')[^1].Replace(".webp", string.Empty, StringComparison.Ordinal)));
        Assert.All(result.Manifest.Derivatives, d => Assert.True(File.Exists(Path.Combine(_folder.Path, d.Path))));
    }

    [Fact]
    public void Makes_the_larger_index_only_sizes_when_the_original_is_not_kept_and_can_read_a_photo_from_memory()
    {
        var bytes = TestPhotos.Porto();
        var path = _folder.Write("a.jpg", bytes);

        var kept = Make(Undescribed(MediaKind.Photo), path, indexOnly: false);
        var indexOnly = Make(Undescribed(MediaKind.Photo), path, indexOnly: true, content: bytes);

        Assert.DoesNotContain(kept.Manifest.Derivatives, d => d.Path.Contains(ThumbnailGenerator.LargePreviewName, StringComparison.Ordinal));
        Assert.Contains(indexOnly.Manifest.Derivatives, d => d.Path.Contains(ThumbnailGenerator.LargePreviewName, StringComparison.Ordinal));
        Assert.Equal(DerivativeStatus.Done, indexOnly.Status);
    }

    [Fact]
    public void Says_a_video_is_pending_when_there_is_no_video_processor()
    {
        var path = _folder.Write("clip.mp4", [1, 2, 3]);

        var result = Make(Undescribed(MediaKind.Video), path, video: null);

        Assert.Equal(DerivativeStatus.Pending, result.Status);
        Assert.Empty(result.Manifest.Derivatives);
    }

    [Fact]
    public void Says_a_tracker_module_is_not_a_video_even_without_a_video_processor()
    {
        var path = _folder.Write("song.mod", TestTrackerModules.Module());

        var result = Make(Undescribed(MediaKind.Video), path, video: null);

        Assert.Equal((DerivativeStatus.NotVideo, NoVideoReason.TrackerModule), (result.Status, result.NoVideo));
    }

    [Fact]
    public void Says_a_file_with_no_video_stream_is_not_a_video_with_the_reason_the_processor_gives()
    {
        var path = _folder.Write("clip.mp4", [1, 2, 3]);
        var processor = new StubVideo { OnProbe = () => throw new NoVideoStreamException(NoVideoReason.CoverArtOnly) };

        var result = Make(Undescribed(MediaKind.Video), path, processor);

        Assert.Equal((DerivativeStatus.NotVideo, NoVideoReason.CoverArtOnly), (result.Status, result.NoVideo));
    }

    [Fact]
    public void Says_a_photo_that_cannot_be_decoded_is_damaged_and_gives_the_decoders_message()
    {
        var bytes = TestPhotos.Porto();
        var random = new Random(7);
        for (var i = bytes.Length / 3; i < bytes.Length - 2; i++)
        {
            bytes[i] = (byte)random.Next(256);
        }

        var path = _folder.Write("broken.jpg", bytes);

        var result = Make(Undescribed(MediaKind.Photo), path);

        Assert.Equal(DerivativeStatus.Damaged, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Problem));
        Assert.Empty(result.Manifest.Derivatives);
    }

    [Fact]
    public void Says_making_a_video_failed_with_the_processors_message()
    {
        var path = _folder.Write("clip.mp4", [1, 2, 3]);
        var processor = new StubVideo { OnProbe = () => throw new VideoProcessingException("ffprobe said no") };

        var result = Make(Undescribed(MediaKind.Video), path, processor);

        Assert.Equal((DerivativeStatus.Failed, "ffprobe said no"), (result.Status, result.Problem));
    }

    [Fact]
    public void Takes_a_videos_size_duration_place_and_derivatives_from_the_processor_and_keeps_a_place_the_file_had()
    {
        var path = _folder.Write("clip.mp4", [1, 2, 3]);
        var derivative = new ManifestDerivative("proxy-720p", "derivatives/x/proxy-720p.mp4", 1280, 720, "video/mp4");
        var processor = new StubVideo
        {
            OnProbe = () => new VideoProbe(1920, 1080, 12.5, Location: new GeoPoint(1, 2)),
            OnGenerate = () => new VideoDerivatives([derivative], "its sound does not decode"),
        };
        var withPlace = Undescribed(MediaKind.Video) with { Location = new GeoPoint(41.1, -8.6) };
        var withoutPlace = Undescribed(MediaKind.Video) with { Location = null };

        var kept = Make(withPlace, path, processor);
        var filled = Make(withoutPlace, path, processor);

        Assert.Equal(DerivativeStatus.Done, kept.Status);
        Assert.Equal((1920, 1080, 12.5), (kept.Manifest.Width, kept.Manifest.Height, kept.Manifest.DurationSeconds));
        Assert.Equal([derivative], kept.Manifest.Derivatives);
        Assert.Equal(("its sound does not decode", VideoSound.Undecodable), (kept.WithoutSound, kept.Sound));
        // ffprobe's position only fills in one the file's own metadata missed.
        Assert.Equal(new GeoPoint(41.1, -8.6), kept.Manifest.Location);
        Assert.Equal(new GeoPoint(1, 2), filled.Manifest.Location);
    }

    private sealed class StubVideo : IVideoProcessor
    {
        public Func<VideoProbe> OnProbe { get; set; } = () => new VideoProbe(640, 480, 1);

        public Func<VideoDerivatives> OnGenerate { get; set; } = () => new VideoDerivatives([]);

        public VideoProbe Probe(string path, CancellationToken cancellationToken = default) => OnProbe();

        public VideoDerivatives Generate(string sourcePath, string workFolder, string relativeFolder, VideoProbe probe, CancellationToken cancellationToken = default) =>
            OnGenerate();

        public bool HasSound(string path, CancellationToken cancellationToken = default) => true;

        public VideoSound CheckSound(string path, CancellationToken cancellationToken = default) => VideoSound.Kept;
    }
}
