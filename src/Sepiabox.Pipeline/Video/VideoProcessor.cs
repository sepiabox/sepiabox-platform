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

/// <summary>What became of a video's sound in its 720p version (D-087).</summary>
public enum VideoSound
{
    /// <summary>The 720p version has the source's first sound stream that decodes (D-105; before it, the first).</summary>
    Kept,

    /// <summary>The source has no sound stream, so neither has the 720p version.</summary>
    NoStream,

    /// <summary>
    /// The source declares sound streams and FFmpeg cannot decode a single frame of any of them (D-084, D-105): the
    /// 720p version was made without sound.
    /// </summary>
    Undecodable,
}

/// <summary>
/// A video's derivatives and what became of its sound. <see cref="WithoutSoundBecause"/> is set when the 720p version
/// was made without sound because its sound stream does not decode (<see cref="VideoSound.Undecodable"/>, D-087):
/// FFmpeg's message for the transcode with sound, so the run can show it.
/// </summary>
public sealed record VideoDerivatives(IReadOnlyList<ManifestDerivative> Derivatives, string? WithoutSoundBecause = null)
{
    public VideoSound Sound { get; init; } = WithoutSoundBecause is null ? VideoSound.Kept : VideoSound.Undecodable;

    /// <summary>
    /// Which sound stream the 720p version has, counting sound streams from 0 (D-105): 0, the first, unless the first
    /// does not decode and a later one does. Meaningless without sound.
    /// </summary>
    public int AudioStream { get; init; }
}

/// <summary>What <c>process</c> needs from a video processor; tests stand in for FFmpeg with it.</summary>
public interface IVideoProcessor
{
    VideoProbe Probe(string path, CancellationToken cancellationToken = default);

    VideoDerivatives Generate(
        string sourcePath, string workFolder, string relativeFolder, VideoProbe probe, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="path"/> has a sound stream, from its header (ffprobe).</summary>
    bool HasSound(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a 720p version of <paramref name="path"/> can keep of its sound: none to keep, sound that decodes, or a sound
    /// stream that does not decode a single frame (D-087). Throws <see cref="VideoProcessingException"/> when the file
    /// cannot be read at the moment.
    /// </summary>
    VideoSound CheckSound(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// The encoder <c>process</c> was asked to use cannot encode at all (D-087), found by encoding a generated half-second
/// clip before any file is processed: an NVIDIA driver older than the NVENC API FFmpeg was built for, no NVIDIA GPU, or
/// an FFmpeg without that encoder. <see cref="Exception.Message"/> holds FFmpeg's message.
/// </summary>
public sealed class VideoEncoderUnavailableException : Exception
{
    public VideoEncoderUnavailableException()
    {
    }

    public VideoEncoderUnavailableException(string message)
        : base(message)
    {
    }

    public VideoEncoderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public VideoEncoderUnavailableException(VideoEncoder encoder, string message, Exception innerException)
        : base(message, innerException)
    {
        Encoder = encoder;
    }

    public VideoEncoder Encoder { get; }
}

/// <summary>
/// The playable H.264 version of a video: its derivative name and the size it is fitted into, never upscaled. 720p in
/// full mode, 1080p in an index-only work folder, where it stands in for the original on screen (S-063, D-088).
/// </summary>
/// <param name="Name">Derivative name, such as <c>proxy-720p</c>.</param>
/// <param name="LongestSide">The longest side it may have, in either orientation.</param>
/// <param name="ShortestSide">The shortest side it may have.</param>
public sealed record VideoVersion(string Name, int LongestSide, int ShortestSide)
{
    public static readonly VideoVersion Hd720 = new(VideoProcessor.ProxyName, 1280, 720);

    public static readonly VideoVersion Hd1080 = new(VideoProcessor.LargeProxyName, 1920, 1080);
}

/// <summary>
/// Makes what the web shows for a video (D-015): a WebP poster and thumbnail taken at 10% of the
/// duration, and a 720p H.264 version with metadata removed. Originals are only read.
/// </summary>
public sealed class VideoProcessor : IVideoProcessor
{
    private readonly FfmpegTools tools;
    private readonly VideoEncoder encoder;
    private readonly IReadOnlyList<string>? videoCodec;
    private readonly VideoVersion version;

    /// <param name="tools">FFmpeg and ffprobe.</param>
    /// <param name="encoder">The H.264 encoder.</param>
    /// <param name="version">The playable version to make: 720p by default (<see cref="VideoVersion.Hd720"/>).</param>
    public VideoProcessor(FfmpegTools tools, VideoEncoder encoder = VideoEncoder.Software, VideoVersion? version = null)
        : this(tools, encoder, null, version)
    {
    }

    /// <summary>For tests: <paramref name="videoCodec"/> replaces the encoder's arguments, such as an encoder that fails.</summary>
    internal VideoProcessor(FfmpegTools tools, VideoEncoder encoder, IReadOnlyList<string>? videoCodec, VideoVersion? version = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        this.tools = tools;
        this.encoder = encoder;
        this.videoCodec = videoCodec;
        this.version = version ?? VideoVersion.Hd720;
    }

    /// <summary>The encoder the 720p versions are made with.</summary>
    public VideoEncoder Encoder => encoder;

    public const string ProxyName = "proxy-720p";

    /// <summary>The playable version of an index-only work folder (S-063).</summary>
    public const string LargeProxyName = "proxy-1080p";

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

    /// <summary>
    /// Encodes a generated half-second clip with picture and sound exactly as a 720p version is encoded, into a
    /// temporary file in <paramref name="workFolder"/> that is then deleted (D-087), so an encoder that cannot work stops
    /// the run before any file, instead of failing every video. Throws <see cref="VideoEncoderUnavailableException"/>
    /// with FFmpeg's message.
    /// </summary>
    public void CheckEncoder(string workFolder, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(workFolder);
        var output = Path.Combine(workFolder, $"encoder-check-{Guid.NewGuid():N}.tmp.mp4");
        try
        {
            Encode(FfmpegArguments.EncoderCheck(encoder, output, videoCodec), cancellationToken);
        }
        catch (VideoProcessingException ex)
        {
            throw new VideoEncoderUnavailableException(encoder, ex.Message, ex);
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file left behind (held by an antivirus scan, say) must not hide the encoder's own outcome.
            }
        }
    }

    public bool HasSound(string path, CancellationToken cancellationToken = default) =>
        FfmpegArguments.CountStreams(Run(tools.Ffprobe, FfmpegArguments.SoundStreams(path), cancellationToken)) > 0;

    /// <summary>
    /// Whether the sound of <paramref name="path"/> is the reason a transcode with sound failed: it is when the file has
    /// sound streams and FFmpeg decodes not a single frame of any of them (D-087, D-105). Decoding stops at the first
    /// frame, so a stream that works is read only as far as that; one that never decodes is read to its end, which only
    /// happens after the transcode has already failed.
    /// </summary>
    public VideoSound CheckSound(string path, CancellationToken cancellationToken = default) => ChooseSound(path, cancellationToken).Sound;

    /// <summary>
    /// The sound stream a 720p version should have (D-105): the first, counting from 0, of which FFmpeg decodes a frame.
    /// Some MPEG files declare an AC-3 stream first that holds a few undecodable packets and a good MP1 stream after it,
    /// and mapping only the first made FFmpeg write nothing. <see cref="VideoSound.NoStream"/> when the file has no sound
    /// stream, <see cref="VideoSound.Undecodable"/> when none decodes (the stream is then 0). Throws
    /// <see cref="VideoProcessingException"/> when the file cannot be read at the moment.
    /// </summary>
    public (VideoSound Sound, int Stream) ChooseSound(string path, CancellationToken cancellationToken = default)
    {
        var streams = FfmpegArguments.CountStreams(Run(tools.Ffprobe, FfmpegArguments.SoundStreams(path), cancellationToken));
        if (streams == 0)
        {
            return (VideoSound.NoStream, 0);
        }

        for (var stream = 0; stream < streams; stream++)
        {
            // Its exit code alone says little: FFmpeg fails when no frame decodes, and can fail after one did. But a file it
            // could not open or read at that moment (a USB drive that dropped out) proves nothing about the sound.
            var (exitCode, frames, errors) = RunCapturing(tools.Ffmpeg, FfmpegArguments.SoundFrame(path, stream), cancellationToken);
            if (FfmpegArguments.CountFrames(frames) > 0)
            {
                return (VideoSound.Kept, stream);
            }

            if (exitCode != 0 && FfmpegArguments.IsInputFailure(errors))
            {
                throw new VideoProcessingException($"ffmpeg could not read the file to check its sound: {FfmpegArguments.Message(errors)}");
            }
        }

        return (VideoSound.Undecodable, 0);
    }

    /// <summary>Writes the poster, thumbnail and 720p version into <paramref name="workFolder"/>/<paramref name="relativeFolder"/>.</summary>
    public VideoDerivatives Generate(
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

        var (width, height) = VideoScale.Fit(probe.Width, probe.Height, version.LongestSide, version.ShortestSide);
        var proxyPath = $"{relativeFolder}/{version.Name}.mp4";
        var proxy = Path.Combine(workFolder, proxyPath);
        var temporary = proxy + ".tmp";
        string? withoutSoundBecause = null;
        VideoSound sound;
        var audioStream = 0;
        try
        {
            Encode(FfmpegArguments.Proxy(sourcePath, width, height, encoder, probe.VideoStream, temporary, videoCodec: videoCodec), cancellationToken);
            sound = HasSound(temporary, cancellationToken) ? VideoSound.Kept : VideoSound.NoStream;
        }
        catch (VideoProcessingException withSound)
        {
            // Only a sound stream that decodes to nothing makes a 720p version without sound (D-087): a camcorder file
            // can declare one that plays as silence (D-084). Any other failure, such as an encoder that cannot open
            // (whose FFmpeg message also ends in "received no packets"), stays a failure with its full message, and
            // the next run tries the file again.
            (VideoSound Sound, int Stream) cause;
            try
            {
                cause = ChooseSound(sourcePath, cancellationToken);
            }
            catch (VideoProcessingException)
            {
                // The check itself failing proves nothing about the sound: the transcode's own failure is reported.
                cause = (VideoSound.Kept, 0);
            }

            if (cause.Sound == VideoSound.NoStream || (cause.Sound == VideoSound.Kept && cause.Stream == 0))
            {
                throw;
            }

            var madeWithLaterSound = false;
            if (cause.Sound == VideoSound.Kept)
            {
                // The first sound stream does not decode but a later one does (D-105): map that one. If it too yields
                // nothing, the 720p version is made without sound, as when none decodes.
                try
                {
                    Encode(FfmpegArguments.Proxy(sourcePath, width, height, encoder, probe.VideoStream, temporary, audioStream: cause.Stream, videoCodec: videoCodec), cancellationToken);
                    (sound, audioStream, madeWithLaterSound) = (VideoSound.Kept, cause.Stream, true);
                }
                catch (VideoProcessingException)
                {
                    sound = VideoSound.Undecodable;
                }
            }
            else
            {
                sound = VideoSound.Undecodable;
            }

            if (!madeWithLaterSound)
            {
                withoutSoundBecause = withSound.Message;
                sound = VideoSound.Undecodable;
                try
                {
                    Encode(FfmpegArguments.Proxy(sourcePath, width, height, encoder, probe.VideoStream, temporary, withAudio: false, videoCodec: videoCodec), cancellationToken);
                }
                catch (VideoProcessingException withoutSound)
                {
                    throw withoutSound.Message == withSound.Message
                        ? withSound
                        : new VideoProcessingException($"{withSound.Message} Without sound: {withoutSound.Message}", withoutSound);
                }
            }
        }

        File.Move(temporary, proxy, overwrite: true);

        return new VideoDerivatives([.. posters, new ManifestDerivative(version.Name, proxyPath, width, height, ProxyContentType)], withoutSoundBecause)
        {
            Sound = sound,
            AudioStream = audioStream,
        };
    }

    /// <summary>
    /// The most NVENC encodes this process runs at once (D-087). Consumer NVIDIA drivers allow about 8 encoding sessions
    /// per system, and <c>--jobs</c> defaults to half the logical processors (8 on a 16-thread computer), so every video
    /// job encoding at once could open more sessions than the driver allows and fail; 4 leaves room for anything else
    /// using the GPU, and a GPU is rarely faster with more sessions than that anyway.
    /// </summary>
    public const int MaxNvencSessions = 4;

    private static readonly SemaphoreSlim s_nvencSessions = new(MaxNvencSessions, MaxNvencSessions);

    // A transcode with the chosen encoder: NVENC encodes wait for one of the process's sessions.
    private void Encode(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (encoder != VideoEncoder.Nvenc)
        {
            Run(tools.Ffmpeg, arguments, cancellationToken);
            return;
        }

        s_nvencSessions.Wait(cancellationToken);
        try
        {
            Run(tools.Ffmpeg, arguments, cancellationToken);
        }
        finally
        {
            s_nvencSessions.Release();
        }
    }

    private static string Run(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var (exitCode, output, errors) = RunCapturing(fileName, arguments, cancellationToken);
        if (exitCode != 0)
        {
            throw new VideoProcessingException($"{Path.GetFileNameWithoutExtension(fileName)} failed: {FfmpegArguments.Message(errors)}");
        }

        return output;
    }

    private static (int ExitCode, string Output, string Errors) RunCapturing(
        string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
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
        return (process.ExitCode, output, errors.GetAwaiter().GetResult());
    }
}

public static class VideoScale
{
    /// <summary>
    /// Fits a video into 720p (or the sides given, such as 1920 and 1080) in either orientation, without upscaling.
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

    /// <summary>The longest message kept from FFmpeg's errors: a few lines, enough for the cause and its consequence.</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>
    /// The 720p version; <paramref name="withAudio"/> false leaves out the sound (D-084, D-087), and
    /// <paramref name="audioStream"/> is the sound stream it takes, counting sound streams from 0 (D-105).
    /// <paramref name="videoCodec"/> replaces the encoder's arguments, for tests.
    /// </summary>
    public static IReadOnlyList<string> Proxy(
        string input, int width, int height, VideoEncoder encoder, int videoStream, string output, bool withAudio = true,
        IReadOnlyList<string>? videoCodec = null, int audioStream = 0) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
        "-i", input,
        "-map", $"0:v:{videoStream}",
        .. withAudio ? (string[])["-map", $"0:a:{audioStream}?"] : ["-an"],
        .. Encoding(width, height, encoder, withAudio, output, videoCodec),
    ];

    /// <summary>
    /// The preflight of D-087: a generated half-second 640x360 clip with picture and stereo sound at 48 kHz, encoded
    /// with exactly the 720p version's encoding arguments into <paramref name="output"/>.
    /// </summary>
    public static IReadOnlyList<string> EncoderCheck(VideoEncoder encoder, string output, IReadOnlyList<string>? videoCodec = null) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
        "-f", "lavfi", "-i", "testsrc=size=640x360:rate=25:duration=0.5",
        "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo",
        "-map", "0:v:0", "-map", "1:a:0", "-t", "0.5",
        .. Encoding(640, 360, encoder, withAudio: true, output, videoCodec),
    ];

    /// <summary>The encoder's arguments: h264_nvenc on an NVIDIA GPU, otherwise libx264.</summary>
    public static IReadOnlyList<string> VideoCodec(VideoEncoder encoder) => encoder == VideoEncoder.Nvenc
        ? ["-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr", "-cq", "28"]
        : ["-c:v", "libx264", "-preset", "veryfast", "-crf", "28"];

    /// <summary>The sound streams of a file, one line each.</summary>
    public static IReadOnlyList<string> SoundStreams(string input) =>
        ["-v", "error", "-select_streams", "a", "-show_entries", "stream=index", "-of", "csv=p=0", input];

    /// <summary>
    /// Decodes the first frame of sound stream number <paramref name="audioStream"/> (counting from 0) and writes one line
    /// for it (FFmpeg's <c>framecrc</c>) on the standard output (D-087); no line when not a single frame decodes.
    /// </summary>
    public static IReadOnlyList<string> SoundFrame(string input, int audioStream = 0) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin",
        "-i", input,
        "-map", $"0:a:{audioStream}", "-frames:a", "1",
        "-f", "framecrc", "-",
    ];

    /// <summary>The lines of ffprobe's <c>csv=p=0</c> output: one per stream.</summary>
    public static int CountStreams(string output) =>
        (output ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    /// <summary>The frames in FFmpeg's <c>framecrc</c> output: every line that is not a <c>#</c> header.</summary>
    public static int CountFrames(string framecrc) =>
        (framecrc ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(line => !line.StartsWith('#'));

    /// <summary>
    /// FFmpeg's errors as one line: every line it printed, in order, joined by " | ", at most
    /// <see cref="MaxMessageLength"/> characters keeping its first 300 and its end. The last line alone is often only the
    /// consequence: "Nothing was written into output file" after an encoder that could not open (D-087).
    /// </summary>
    public static string Message(string errors)
    {
        var lines = (errors ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var message = string.Join(" | ", lines);
        // Both ends: the cause is usually the first line, and the outcome the last.
        return message.Length <= MaxMessageLength
            ? message
            : message[..MessageStart] + "…" + message[^(MaxMessageLength - MessageStart - 1)..];
    }

    /// <summary>
    /// Whether FFmpeg's errors say it could not open or read its input, rather than decode it: then a check of the
    /// sound proves nothing (D-087).
    /// </summary>
    public static bool IsInputFailure(string errors) =>
        s_inputFailures.Any(phrase => (errors ?? string.Empty).Contains(phrase, StringComparison.OrdinalIgnoreCase));

    // FFmpeg's own words for an input it cannot open or read (its "Error opening input" and the system's errors).
    private static readonly string[] s_inputFailures =
        ["Error opening input", "Input/output error", "No such file or directory", "Permission denied", "I/O error"];

    // The start kept from a long message; the rest of MaxMessageLength comes from its end.
    private const int MessageStart = 300;

    private static IEnumerable<string> Encoding(
        int width, int height, VideoEncoder encoder, bool withAudio, string output, IReadOnlyList<string>? videoCodec) =>
    [
        // Drops metadata such as the GPS position phones write into videos.
        "-map_metadata", "-1",
        "-vf", Filter(width, height),
        .. videoCodec ?? VideoCodec(encoder),
        "-pix_fmt", "yuv420p",
        .. withAudio ? (string[])["-c:a", "aac", "-b:a", "96k"] : [],
        "-movflags", "+faststart",
        "-f", "mp4",
        output,
    ];

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
