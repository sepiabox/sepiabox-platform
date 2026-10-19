using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Pipeline.Video;

public enum VideoEncoder
{
    /// <summary>libx264 on the CPU.</summary>
    Software,

    /// <summary>h264_nvenc on an NVIDIA GPU.</summary>
    Nvenc,
}

/// <summary>Size as shown on screen, after the rotation phones record instead of rotating pixels.</summary>
public sealed record VideoProbe(int Width, int Height, double? DurationSeconds);

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

/// <summary>
/// Makes what the web shows for a video (D-015): a WebP poster and thumbnail taken at 10% of the
/// duration, and a 720p H.264 version with metadata removed. Originals are only read.
/// </summary>
public sealed class VideoProcessor(FfmpegTools tools, VideoEncoder encoder = VideoEncoder.Software)
{
    public const string ProxyName = "proxy-720p";
    public const string ProxyContentType = "video/mp4";

    public static readonly IReadOnlyList<ThumbnailSize> PosterSizes =
    [
        new("poster", 1280),
        new("thumb-256", 256),
    ];

    public VideoProbe Probe(string path, CancellationToken cancellationToken = default)
    {
        var json = Run(tools.Ffprobe, FfmpegArguments.Probe(path), cancellationToken);
        return FfmpegArguments.ParseProbe(json);
    }

    /// <summary>Writes the poster, thumbnail and 720p version into <paramref name="workFolder"/>/<paramref name="relativeFolder"/>.</summary>
    public IReadOnlyList<ManifestDerivative> Generate(
        string sourcePath, string workFolder, string relativeFolder, VideoProbe probe, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(workFolder, relativeFolder);
        Directory.CreateDirectory(folder);

        var frame = Path.Combine(folder, "poster-frame.tmp.png");
        var posterAt = (probe.DurationSeconds ?? 0) * 0.1;
        Run(tools.Ffmpeg, FfmpegArguments.Frame(sourcePath, posterAt, frame), cancellationToken);
        IReadOnlyList<ManifestDerivative> posters;
        try
        {
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
        Run(tools.Ffmpeg, FfmpegArguments.Proxy(sourcePath, width, height, encoder, temporary), cancellationToken);
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
    public static IReadOnlyList<string> Probe(string input) =>
    [
        "-v", "error",
        "-select_streams", "v:0",
        "-show_entries", "stream=width,height:stream_tags=rotate:stream_side_data=rotation:format=duration",
        "-of", "json",
        input,
    ];

    public static IReadOnlyList<string> Frame(string input, double atSeconds, string output) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
        "-ss", atSeconds.ToString("0.###", CultureInfo.InvariantCulture),
        "-i", input,
        "-frames:v", "1",
        "-f", "image2", "-c:v", "png",
        output,
    ];

    public static IReadOnlyList<string> Proxy(string input, int width, int height, VideoEncoder encoder, string output)
    {
        List<string> arguments =
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-i", input,
            "-map", "0:v:0", "-map", "0:a:0?",
            // Drops metadata such as the GPS position phones write into videos.
            "-map_metadata", "-1",
            "-vf", $"scale={width}:{height}",
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
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
        {
            throw new VideoProcessingException("No video stream found.");
        }

        var stream = streams[0];
        var width = stream.GetProperty("width").GetInt32();
        var height = stream.GetProperty("height").GetInt32();
        if (Math.Abs(Rotation(stream)) % 180 == 90)
        {
            (width, height) = (height, width);
        }

        double? duration = root.TryGetProperty("format", out var format)
            && format.TryGetProperty("duration", out var value)
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
                ? Math.Round(seconds, 3)
                : null;

        return new VideoProbe(width, height, duration);
    }

    // Newer files carry a display matrix in side data; older ones a "rotate" tag.
    private static int Rotation(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var sideData))
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var rotation) && rotation.TryGetInt32(out var degrees))
                {
                    return degrees;
                }
            }
        }

        return stream.TryGetProperty("tags", out var tags)
            && tags.TryGetProperty("rotate", out var rotate)
            && int.TryParse(rotate.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tagged)
                ? tagged
                : 0;
    }
}
