using System.Diagnostics.CodeAnalysis;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Processing;

/// <param name="Root">Folder or drive to read.</param>
/// <param name="OutputPath">Work folder for manifests, derivatives and progress.</param>
/// <param name="SourceName">Name of the drive or folder; null to use the folder name.</param>
/// <param name="FfmpegFolder">Folder with <c>ffmpeg</c> and <c>ffprobe</c>; null to look on the PATH.</param>
/// <param name="Encoder">H.264 encoder for the 720p versions.</param>
public sealed record ProcessOptions(
    string Root,
    string OutputPath,
    string? SourceName,
    string? FfmpegFolder = null,
    VideoEncoder Encoder = VideoEncoder.Software)
{
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

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--out" or "--source-name" or "--ffmpeg" or "--hw" when i + 1 >= args.Count:
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
            root, outputPath, string.IsNullOrWhiteSpace(sourceName) ? null : sourceName.Trim(), ffmpegFolder, encoder);
        error = null;
        return true;
    }
}
