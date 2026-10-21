using System.Diagnostics.CodeAnalysis;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Processing;

/// <param name="Root">Folder or drive to read.</param>
/// <param name="OutputPath">Work folder for manifests, derivatives and progress.</param>
/// <param name="SourceName">Name of the drive or folder; null to use the folder name.</param>
/// <param name="FfmpegFolder">Folder with <c>ffmpeg</c> and <c>ffprobe</c>; null to look on the PATH.</param>
/// <param name="Encoder">H.264 encoder for the 720p versions.</param>
/// <param name="ModelFolder">Folder with the downloaded models; null for the default (<c>models download</c>).</param>
/// <param name="RetryLeftOut">Read again the files earlier runs left out as holding no video, even when unchanged.</param>
/// <param name="ListLeftOut">List every file left out by earlier runs, not just how many.</param>
/// <param name="Faces">Detect faces (S-018). Off by default: faces are biometric data, so the user opts in.</param>
/// <param name="Jobs">Files processed at once (<c>--jobs</c>); null for <see cref="DefaultJobs"/>.</param>
/// <param name="VideoJobs">Videos read from the drive at once (<c>--video-jobs</c>); null for 1.</param>
/// <param name="IndexOnly">
/// The originals stay where they are and are never uploaded (S-063, D-088); previews are larger. Belongs to the work
/// folder: the first run records it, and a later run asking for the other mode is refused.
/// </param>
public sealed record ProcessOptions(
    string Root,
    string OutputPath,
    string? SourceName,
    string? FfmpegFolder = null,
    VideoEncoder Encoder = VideoEncoder.Software,
    string? ModelFolder = null,
    bool RetryLeftOut = false,
    bool ListLeftOut = false,
    bool Faces = false,
    int? Jobs = null,
    int? VideoJobs = null,
    bool IndexOnly = false)
{
    /// <summary>The mode this run asks for.</summary>
    public WorkFolderMode Mode => IndexOnly ? WorkFolderMode.IndexOnly : WorkFolderMode.KeepsOriginals;

    /// <summary>The most files <c>--jobs</c> may process at once.</summary>
    public const int MaxJobs = 64;

    /// <summary>Half the logical processors, at least 1: the work is CPU-bound, and the machine stays usable (D-078).</summary>
    public static int DefaultJobs => Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Files processed at once: <c>--jobs</c>, or <see cref="DefaultJobs"/>.</summary>
    public int EffectiveJobs => Jobs ?? DefaultJobs;

    /// <summary>
    /// Videos read from the drive at once (hashed, probed or transcoded): <c>--video-jobs</c>, 1 by default, since a
    /// video is too large to read ahead into memory, FFmpeg already uses every core, and a hard disk is slower when two
    /// files are read at once. Never more than <see cref="EffectiveJobs"/>.
    /// </summary>
    public int EffectiveVideoJobs => Math.Min(VideoJobs ?? 1, EffectiveJobs);

    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out ProcessOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        string? root = null;
        string? outputPath = null;
        string? sourceName = null;
        string? ffmpegFolder = null;
        var encoder = VideoEncoder.Software;
        string? modelFolder = null;
        var retryLeftOut = false;
        var listLeftOut = false;
        var faces = false;
        int? jobs = null;
        int? videoJobs = null;
        var indexOnly = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--out" or "--source-name" or "--ffmpeg" or "--hw" or "--models" or "--jobs" or "--video-jobs" when i + 1 >= args.Count:
                    error = $"{args[i]} needs a value.";
                    return false;

                case "--out":
                    outputPath = args[++i];
                    break;

                case "--source-name":
                    sourceName = args[++i];
                    break;

                case "--ffmpeg":
                    ffmpegFolder = args[++i];
                    break;

                case "--models":
                    modelFolder = args[++i];
                    break;

                case "--retry-left-out":
                    retryLeftOut = true;
                    break;

                case "--list-left-out":
                    listLeftOut = true;
                    break;

                case "--faces":
                    faces = true;
                    break;

                case "--index-only":
                    indexOnly = true;
                    break;

                case "--jobs" or "--video-jobs":
                    var name = args[i];
                    if (!int.TryParse(args[++i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count)
                        || count < 1 || count > MaxJobs)
                    {
                        error = $"{name} needs a whole number from 1 to {MaxJobs}.";
                        return false;
                    }

                    if (name == "--jobs")
                    {
                        jobs = count;
                    }
                    else
                    {
                        videoJobs = count;
                    }

                    break;

                case "--hw" when args[i + 1] == "nvenc":
                    encoder = VideoEncoder.Nvenc;
                    i++;
                    break;

                case "--hw":
                    error = $"Unknown encoder '{args[i + 1]}'. The only hardware encoder supported is nvenc.";
                    return false;

                case var arg when arg.StartsWith('-'):
                    error = $"Unknown option '{arg}'.";
                    return false;

                case var arg when root is null:
                    root = arg;
                    break;

                default:
                    error = "Only one folder can be processed at a time.";
                    return false;
            }
        }

        if (root is null)
        {
            error = "Tell process which folder to read.";
            return false;
        }

        if (outputPath is null)
        {
            error = "Tell process where to write its results with --out <folder>.";
            return false;
        }

        options = new ProcessOptions(
            root, outputPath, string.IsNullOrWhiteSpace(sourceName) ? null : sourceName.Trim(), ffmpegFolder, encoder, modelFolder,
            retryLeftOut, listLeftOut, faces, jobs, videoJobs, indexOnly);
        error = null;
        return true;
    }
}
