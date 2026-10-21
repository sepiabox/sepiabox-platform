using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Tests;

/// <summary>process --index-only with the real FFmpeg (S-063): the 1080p version and the larger photo preview, in one work folder.</summary>
[Trait("Category", "Integration")]
public sealed class IndexOnlyVideoTests : IDisposable
{
    private static readonly FfmpegTools s_tools = FfmpegTools.Find(null)
        ?? throw new InvalidOperationException("Integration tests need ffmpeg and ffprobe on the PATH.");

    private static readonly string s_ffmpegFolder = Path.GetDirectoryName(s_tools.Ffmpeg)!;

    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void Lists_the_large_derivatives_of_a_photo_and_a_video_and_none_of_the_small_ones()
    {
        _drive.Write("big.jpg", TestPhotos.Sized(4000, 3000));
        MakeVideo(Path.Combine(_drive.Path, "party.mp4"), 1920, 1080, seconds: 2);

        var result = Process(indexOnly: true);

        Assert.Empty(result.Problems);
        Assert.Empty(result.WithoutSound);
        var manifests = ReadManifests();
        Assert.Equal(2, manifests.Count);
        Assert.All(manifests, m => Assert.False(m.IsOriginalStored));

        var photo = Assert.Single(manifests, m => m.Kind == MediaKind.Photo);
        Assert.Equal(
            [("thumb-256", 256, 192), ("preview-2048", 2048, 1536)],
            photo.Derivatives.Select(d => (d.Name, d.Width, d.Height)));

        var video = Assert.Single(manifests, m => m.Kind == MediaKind.Video);
        Assert.Equal(
            [("poster", 1280, 720), ("thumb-256", 256, 144), ("proxy-1080p", 1920, 1080)],
            video.Derivatives.Select(d => (d.Name, d.Width, d.Height)));
        Assert.DoesNotContain(manifests.SelectMany(m => m.Derivatives), d => d.Name is "preview-1280" or "proxy-720p");
        Assert.Empty(Directory.GetFiles(Path.Combine(_work.Path, "derivatives"), "*720p*", SearchOption.AllDirectories));

        var proxy = Assert.Single(video.Derivatives, d => d.Name == "proxy-1080p");
        Assert.Equal("video/mp4", proxy.ContentType);
        var probe = new VideoProcessor(s_tools).Probe(Path.Combine(_work.Path, proxy.Path));
        Assert.Equal((1920, 1080), (probe.Width, probe.Height));
        // The same sound rules as the 720p version (D-084, D-087): the generated sound is kept.
        Assert.True(new VideoProcessor(s_tools).HasSound(Path.Combine(_work.Path, proxy.Path)));
        Assert.Equal(VideoSound.Kept, StoredSound(video));

        // The schema validates what was written.
        var validator = new ManifestValidator();
        foreach (var file in Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json"))
        {
            Assert.True(validator.TryValidate(File.ReadAllText(file), out _, out var invalid), invalid);
        }
    }

    [Theory]
    [InlineData(3840, 2160, 1920, 1080)]
    [InlineData(2160, 3840, 1080, 1920)]
    [InlineData(1280, 720, 1280, 720)]
    public void Fits_a_video_inside_1080p_in_either_orientation_without_upscaling(int width, int height, int expectedWidth, int expectedHeight)
    {
        MakeVideo(Path.Combine(_drive.Path, "clip.mp4"), width, height, seconds: 1);

        Process(indexOnly: true);

        var proxy = Assert.Single(Assert.Single(ReadManifests()).Derivatives, d => d.Name == "proxy-1080p");
        Assert.Equal((expectedWidth, expectedHeight), (proxy.Width, proxy.Height));
        var probe = new VideoProcessor(s_tools).Probe(Path.Combine(_work.Path, proxy.Path));
        Assert.Equal((expectedWidth, expectedHeight), (probe.Width, probe.Height));
    }

    [Fact]
    public void Redoes_nothing_for_an_unchanged_index_only_video()
    {
        MakeVideo(Path.Combine(_drive.Path, "clip.mp4"), 640, 360, seconds: 1);
        Process(indexOnly: true);
        var proxy = Path.Combine(_work.Path, Assert.Single(ReadManifests()).Derivatives.Single(d => d.Name == "proxy-1080p").Path);
        var written = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(proxy, written);

        var second = Process(indexOnly: true);

        Assert.Equal((0, 1), (second.Processed, second.Skipped));
        Assert.Equal(written, File.GetLastWriteTimeUtc(proxy));
    }

    [Fact]
    public void A_full_mode_work_folder_still_makes_the_720p_version()
    {
        MakeVideo(Path.Combine(_drive.Path, "party.mp4"), 1920, 1080, seconds: 1);

        Process(indexOnly: false);

        var video = Assert.Single(ReadManifests());
        Assert.Equal(["poster", "thumb-256", "proxy-720p"], video.Derivatives.Select(d => d.Name));
        Assert.True(video.IsOriginalStored);
    }

    private VideoSound? StoredSound(Manifest video)
    {
        using var state = new StateStore(Path.Combine(_work.Path, "state.db"));
        return state.Get(Path.Combine(_drive.Path, "party.mp4"))?.Sound;
    }

    private ProcessResult Process(bool indexOnly) =>
        ProcessCommand.Run(new ProcessOptions(_drive.Path, _work.Path, "HD", FfmpegFolder: s_ffmpegFolder, ModelFolder: TestModels.None, IndexOnly: indexOnly));

    private List<Manifest> ReadManifests() =>
        [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];

    private static void MakeVideo(string path, int width, int height, int seconds)
    {
        var info = new System.Diagnostics.ProcessStartInfo(s_tools.Ffmpeg) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc=duration={seconds}:size={width}x{height}:rate=25",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest",
            path,
        })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(info)!;
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"ffmpeg failed: {errors.Result}");
    }
}
