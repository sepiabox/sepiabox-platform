using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Video;

public enum VideoEncoder
{
    /// <summary>libx264 on the CPU.</summary>
    Software,

    /// <summary>h264_nvenc on an NVIDIA GPU.</summary>
    Nvenc,
}

/// <summary>
/// Size as shown on screen: after the rotation phones record instead of rotating pixels, and after the
/// pixel aspect ratio of anamorphic camcorder and DVD video (720x576 stored, 1024x576 shown for 16:9).
/// <c>VideoStream</c> is which video stream holds the footage, counting video streams only, so cover art is skipped.
/// <c>Location</c> is the ISO 6709 position in the container's tags (<see cref="Iso6709"/>), for the videos whose
/// position MetadataExtractor does not expose, such as an MP4's 3GPP <c>loci</c> box.
/// </summary>
public sealed record VideoProbe(int Width, int Height, double? DurationSeconds, int VideoStream = 0, GeoPoint? Location = null);

public sealed class VideoProcessingException : Exception
{
    public VideoProcessingException()
    {
    }

    public VideoProcessingException(string message)
        : base(message)
    {
    }

    public VideoProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The file holds no video, such as a sound-only recording or audio with cover art; <see cref="Reason"/> says which.
/// Not a <see cref="VideoProcessingException"/>: it is not a failure, so no handler of those may swallow it.
/// </summary>
public sealed class NoVideoStreamException : Exception
{
    public NoVideoStreamException()
        : this(NoVideoReason.NoStream)
    {
    }

    public NoVideoStreamException(string message)
        : this(NoVideoReason.NoStream, message)
    {
    }

    public NoVideoStreamException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public NoVideoStreamException(NoVideoReason reason, string? message = null)
        : base(message ?? (reason == NoVideoReason.CoverArtOnly ? "Only cover art, no video stream." : "No video stream."))
    {
        Reason = reason;
    }

    public NoVideoReason Reason { get; } = NoVideoReason.NoStream;
}

/// <summary>Where <c>ffmpeg</c> and <c>ffprobe</c> live. FFmpeg is not bundled; the user installs it.</summary>
public sealed record FfmpegTools(string Ffmpeg, string Ffprobe)
{
    /// <summary>Looks in <paramref name="folder"/> when given, otherwise on the PATH. Null when either tool is missing.</summary>
    public static FfmpegTools? Find(string? folder)
    {
        var folders = folder is not null
            ? [folder]
            : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var ffmpeg = FindIn(folders, "ffmpeg");
        var ffprobe = FindIn(folders, "ffprobe");
        return ffmpeg is not null && ffprobe is not null ? new FfmpegTools(ffmpeg, ffprobe) : null;
    }

    private static string? FindIn(IEnumerable<string> folders, string tool)
    {
        var fileName = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        return folders.Select(folder => Path.Combine(folder, fileName)).FirstOrDefault(File.Exists);
    }
}

/// <summary>What <c>process</c> needs from a video processor; tests stand in for FFmpeg with it.</summary>
public interface IVideoProcessor
{
    VideoProbe Probe(string path, CancellationToken cancellationToken = default);

    IReadOnlyList<ManifestDerivative> Generate(
        string sourcePath, string workFolder, string relativeFolder, VideoProbe probe, CancellationToken cancellationToken = default);
}

/// <summary>
/// Makes what the web shows for a video (D-015): a WebP poster and thumbnail taken at 10% of the
/// duration, and a 720p H.264 version with metadata removed. Originals are only read.
/// </summary>
public sealed class VideoProcessor(FfmpegTools tools, VideoEncoder encoder = VideoEncoder.Software) : IVideoProcessor
{
    public const string ProxyName = "proxy-720p";
    public const string ProxyContentType = "video/mp4";

    /// <summary>The video's still, which is also what its CLIP embedding is computed from (S-015).</summary>
    public const string PosterName = "poster";

    public static readonly IReadOnlyList<ThumbnailSize> PosterSizes =
    [
        new(PosterName, 1280),
        new("thumb-256", 256),
    ];

    /// <summary>
    /// The size, duration and video stream of <paramref name="path"/>. A file that is left out is never catalogued,
    /// so <see cref="NoVideoStreamException"/> is thrown only on strong evidence:
    /// <list type="bullet">
    /// <item>When FFmpeg's default probe finds no video stream, or one without a picture size, a second probe reads
    /// far more of the file: an MPEG file can start with a minute of sound before its first picture.</item>
    /// <item>A file that looks like MPEG (<see cref="MpegSignature"/>) and still has no video is reported as
    /// damaged (<see cref="VideoProcessingException"/>), not left out.</item>
    /// </list>
    /// Cover art is the file's whole content, so it is not probed again.
    /// </summary>
    public VideoProbe Probe(string path, CancellationToken cancellationToken = default)
    {
        // A failing ffprobe throws from Run and is not probed again: the file cannot be read, however much of it.
        var json = Run(tools.Ffprobe, FfmpegArguments.Probe(path), cancellationToken);
        bool firstSawSomething;
        try
        {
            return FfmpegArguments.ParseProbe(json);
        }
        catch (Exception ex) when (ex is VideoProcessingException or NoVideoStreamException { Reason: NoVideoReason.NoStream })
        {
            // No video stream, no picture size, or output of an unexpected shape: read more of the file. Only the
            // first case is evidence of no video; the others mean the first probe saw a stream it could not use.
            firstSawSomething = ex is VideoProcessingException;
        }

        json = Run(tools.Ffprobe, FfmpegArguments.Probe(path, thorough: true), cancellationToken);
        try
        {
            return FfmpegArguments.ParseProbe(json);
        }
        catch (NoVideoStreamException ex) when (ex.Reason == NoVideoReason.NoStream)
        {
            if (firstSawSomething)
            {
                throw new VideoProcessingException("FFmpeg first saw a video stream without a picture size, then none with a longer probe; the file may be damaged.");
            }

            if (LooksLikeMpeg(path))
            {
                throw new VideoProcessingException("FFmpeg finds no video stream in this MPEG file, even with a longer probe; it may be damaged.");
            }

            throw new NoVideoStreamException(NoVideoReason.NoStream, "No video stream, even with a longer probe.");
        }
    }

    // A file whose start cannot be read is a problem: leaving it out would need proof that it is not MPEG.
    private static bool LooksLikeMpeg(string path)
    {
        try
        {
            return MpegSignature.IsMatch(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VideoProcessingException($"Could not read the start of the file: {ex.Message}", ex);
        }
    }

    /// <summary>Writes the poster, thumbnail and 720p version into <paramref name="workFolder"/>/<paramref name="relativeFolder"/>.</summary>
    public IReadOnlyList<ManifestDerivative> Generate(
        string sourcePath, string workFolder, string relativeFolder, VideoProbe probe, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(workFolder, relativeFolder);
        Directory.CreateDirectory(folder);

        var frame = Path.Combine(folder, "poster-frame.tmp.png");
        var posterAt = (probe.DurationSeconds ?? 0) * 0.1;

        // A frame left by a killed run must not pass for this one.
        bool TakeFrame(double atSeconds)
        {
            File.Delete(frame);
            Run(tools.Ffmpeg, FfmpegArguments.Frame(sourcePath, atSeconds, probe.Width, probe.Height, probe.VideoStream, frame), cancellationToken);
            var written = new FileInfo(frame);
            return written.Exists && written.Length > 0;
        }

        IReadOnlyList<ManifestDerivative> posters;
        try
        {
            // When the seek decodes no frame (a transport stream with a single keyframe, a video shorter than
            // its sound), FFmpeg writes nothing and, depending on the build, succeeds or fails. The first frame
            // is always there; only its failure is reported.
            bool taken;
            try
            {
                taken = TakeFrame(posterAt);
            }
            catch (VideoProcessingException) when (posterAt > 0)
            {
                taken = false;
            }

            if (!taken && posterAt > 0)
            {
                taken = TakeFrame(0);
            }

            if (!taken)
            {
                throw new VideoProcessingException("FFmpeg wrote no poster frame.");
            }

            posters = ThumbnailGenerator.Generate(frame, workFolder, relativeFolder, PosterSizes);
        }
        finally
        {
            File.Delete(frame);
        }

        var (width, height) = VideoScale.Fit(probe.Width, probe.Height);
        var proxyPath = $"{relativeFolder}/{ProxyName}.mp4";
        var proxy = Path.Combine(workFolder, proxyPath);
        var temporary = proxy + ".tmp";
        Run(tools.Ffmpeg, FfmpegArguments.Proxy(sourcePath, width, height, encoder, probe.VideoStream, temporary), cancellationToken);
        File.Move(temporary, proxy, overwrite: true);

        return [.. posters, new ManifestDerivative(ProxyName, proxyPath, width, height, ProxyContentType)];
    }

    private static string Run(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        // Arguments go through ArgumentList, never a shell string, so file names cannot inject options.
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new VideoProcessingException($"Could not start {fileName}.");
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        });

        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            var reason = errors.GetAwaiter().GetResult().Trim().Split('\n').LastOrDefault()?.Trim();
            throw new VideoProcessingException($"{Path.GetFileNameWithoutExtension(fileName)} failed: {reason}");
        }

        return output;
    }
}

public static class VideoScale
{
    /// <summary>
    /// Fits a video into 720p in either orientation (longest side 1280, shortest 720) without upscaling.
    /// H.264 with 4:2:0 chroma needs even dimensions, so both are rounded down to even.
    /// </summary>
    public static (int Width, int Height) Fit(int width, int height, int longest = 1280, int shortest = 720)
    {
        var factor = Math.Min(1.0, Math.Min((double)longest / Math.Max(width, height), (double)shortest / Math.Min(width, height)));
        return (Even(width * factor), Even(height * factor));
    }

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value, MidpointRounding.AwayFromZero) / 2 * 2);
}

/// <summary>Command lines for ffprobe and ffmpeg, as argument lists.</summary>
public static class FfmpegArguments
{
    // The Apple key first: iPhones write it, and it is the one Photos shows. FFmpeg reports the ©xyz atom and the
    // 3GPP loci box as "location".
    private static readonly string[] s_locationTags = ["com.apple.quicktime.location.ISO6709", "location"];

    /// <summary>
    /// <paramref name="thorough"/> reads up to 100 MB and 100 seconds before deciding which streams the file
    /// has, instead of FFmpeg's few megabytes and seconds.
    /// </summary>
    public static IReadOnlyList<string> Probe(string input, bool thorough = false) =>
    [
        "-v", "error",
        .. thorough ? (string[])["-probesize", "100M", "-analyzeduration", "100M"] : [],
        "-select_streams", "v",
        "-show_entries", $"stream=width,height,sample_aspect_ratio:stream_disposition=attached_pic:stream_tags=rotate:stream_side_data=rotation:format=duration:format_tags={string.Join(',', s_locationTags)}",
        "-of", "json",
        input,
    ];

    /// <summary>
    /// Deinterlaces only frames flagged as interlaced (camcorder footage) and resamples to square pixels at
    /// the display size, so the still and the 720p version look like the camera's screen. FFmpeg rotates
    /// before applying filters, so the size is the rotated one.
    /// </summary>
    public static string Filter(int width, int height) => $"yadif=deint=interlaced,scale={width}:{height},setsar=1";

    public static IReadOnlyList<string> Frame(string input, double atSeconds, int width, int height, int videoStream, string output) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
        "-ss", atSeconds.ToString("0.###", CultureInfo.InvariantCulture),
        "-i", input,
        "-map", $"0:v:{videoStream}",
        "-vf", Filter(width, height),
        "-frames:v", "1",
        "-f", "image2", "-c:v", "png",
        output,
    ];

    public static IReadOnlyList<string> Proxy(string input, int width, int height, VideoEncoder encoder, int videoStream, string output)
    {
        List<string> arguments =
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-i", input,
            "-map", $"0:v:{videoStream}", "-map", "0:a:0?",
            // Drops metadata such as the GPS position phones write into videos.
            "-map_metadata", "-1",
            "-vf", Filter(width, height),
        ];
        arguments.AddRange(encoder == VideoEncoder.Nvenc
            ? ["-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr", "-cq", "28"]
            : ["-c:v", "libx264", "-preset", "veryfast", "-crf", "28"]);
        arguments.AddRange(
        [
            "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "96k",
            "-movflags", "+faststart",
            "-f", "mp4",
            output,
        ]);
        return arguments;
    }

    public static VideoProbe ParseProbe(string json)
    {
        try
        {
            return ParseProbeDocument(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Output of an unexpected shape is one file's problem, never a reason to stop the run.
            throw new VideoProcessingException($"ffprobe's output could not be read: {ex.Message}", ex);
        }
    }

    private static VideoProbe ParseProbeDocument(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        // Probe asks for video streams only (-select_streams v), so every stream here is a video stream; an
        // attached picture (cover art in a music file or a video container) is one too, so they are told apart.
        var all = root.TryGetProperty("streams", out var streams)
            ? streams.EnumerateArray().Select((stream, index) => (Stream: stream, Index: index)).ToList()
            : [];
        if (all.Count == 0)
        {
            throw new NoVideoStreamException(NoVideoReason.NoStream);
        }

        if (all.All(s => IsAttachedPicture(s.Stream)))
        {
            throw new NoVideoStreamException(NoVideoReason.CoverArtOnly);
        }

        var footage = all.Where(s => !IsAttachedPicture(s.Stream)).ToList();

        var (stream, videoStream) = footage[0];
        if (!TryGetSize(stream, "width", out var width) || !TryGetSize(stream, "height", out var height))
        {
            throw new VideoProcessingException("ffprobe reported no picture size.");
        }

        width = ApplyPixelAspectRatio(width, stream);
        if (Math.Abs(Rotation(stream)) % 180 == 90)
        {
            (width, height) = (height, width);
        }

        double? duration = root.TryGetProperty("format", out var format)
            && format.TryGetProperty("duration", out var value)
            && value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
                ? Math.Round(seconds, 3)
                : null;

        return new VideoProbe(width, height, duration, videoStream, Location(root));
    }

    private static GeoPoint? Location(JsonElement root)
    {
        if (!root.TryGetProperty("format", out var format) || !format.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return s_locationTags
            .Select(name => tags.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? Iso6709.Parse(value.GetString()) : null)
            .FirstOrDefault(point => point is not null);
    }

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition)
        && disposition.TryGetProperty("attached_pic", out var attached)
        && attached.ValueKind == JsonValueKind.Number
        && attached.TryGetInt32(out var flag)
        && flag == 1;

    private static bool TryGetSize(JsonElement stream, string name, out int value)
    {
        value = 0;
        return stream.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value)
            && value > 0;
    }

    // The pixel aspect ratio applies to stored pixels, before rotation. "0:1" means unknown; a ratio far
    // outside what any camera records is treated as a broken header rather than squashing the picture.
    private static int ApplyPixelAspectRatio(int width, JsonElement stream)
    {
        if (!stream.TryGetProperty("sample_aspect_ratio", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return width;
        }

        var parts = value.GetString()!.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var numerator)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var denominator)
            || numerator <= 0 || denominator <= 0)
        {
            return width;
        }

        var ratio = (double)numerator / denominator;
        return ratio is >= 0.25 and <= 4
            ? Math.Max(1, (int)Math.Round(width * ratio, MidpointRounding.AwayFromZero))
            : width;
    }

    // Newer files carry a display matrix in side data; older ones a "rotate" tag.
    private static int Rotation(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var sideData))
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var rotation) && rotation.ValueKind == JsonValueKind.Number && rotation.TryGetInt32(out var degrees))
                {
                    return degrees;
                }
            }
        }

        return stream.TryGetProperty("tags", out var tags)
            && tags.TryGetProperty("rotate", out var rotate)
            && rotate.ValueKind == JsonValueKind.String
            && int.TryParse(rotate.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tagged)
                ? tagged
                : 0;
    }
}
