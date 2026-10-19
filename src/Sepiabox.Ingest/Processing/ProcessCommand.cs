using System.Diagnostics;
using ImageMagick;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Processing;

public sealed record ProcessResult(
    string Root,
    string ManifestFolder,
    TimeSpan Duration,
    int Processed,
    int Skipped,
    IReadOnlyList<ScanProblem> Problems,
    int VideosPending = 0,
    string? Warning = null);

/// <summary>
/// Describes every photo and video in a folder tree as one manifest per distinct content (D-005, D-014),
/// with the thumbnails, posters and 720p versions the web shows instead of the originals (D-015).
/// Source files are only read; results go to the work folder.
/// </summary>
public static class ProcessCommand
{
    public const string LocalSourceType = "local";

    public static ProcessResult Run(ProcessOptions options, IProgress<int>? filesDone = null, CancellationToken cancellationToken = default)
    {
        var root = FileTree.ResolveRoot(options.Root);
        var output = Path.GetFullPath(options.OutputPath);
        var manifestFolder = Path.Combine(output, "manifests");
        Directory.CreateDirectory(manifestFolder);

        // A drive root such as "E:\" has no folder name to fall back on.
        var sourceName = options.SourceName ?? NullIfEmpty(Path.GetFileName(Path.TrimEndingDirectorySeparator(root))) ?? root;
        var outputPrefix = Path.TrimEndingDirectorySeparator(output) + Path.DirectorySeparatorChar;

        var ffmpeg = FfmpegTools.Find(options.FfmpegFolder);
        var video = ffmpeg is null ? null : new VideoProcessor(ffmpeg, options.Encoder);

        using var state = new StateStore(Path.Combine(output, "state.db"));
        var stopwatch = Stopwatch.StartNew();
        var problems = new List<ScanProblem>();
        int processed = 0, skipped = 0, pending = 0;

        foreach (var file in FileTree.Enumerate(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var kind = MediaTypes.Classify(file.Path);
            if (kind == MediaKind.Other || file.Path.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var modifiedTicks = file.ModifiedAt.UtcTicks;
            var previous = state.Get(file.Path);
            var unchanged = previous is not null && previous.Size == file.Size && previous.ModifiedTicks == modifiedTicks;
            if (unchanged && previous!.Status == FileStatus.Done)
            {
                skipped++;
                continue;
            }

            // Still no FFmpeg: re-reading a large unchanged video would change nothing.
            if (unchanged && previous!.Status == FileStatus.Pending && video is null)
            {
                pending++;
                continue;
            }

            try
            {
                var sha256 = MediaFile.Sha256Hex(file.Path);
                var source = new ManifestSource(
                    LocalSourceType,
                    sourceName,
                    Path.GetRelativePath(root, file.Path).Replace('\\', '/'),
                    file.ModifiedAt);

                if (previous?.Sha256 is { } oldSha256 && oldSha256 != sha256)
                {
                    RemoveSource(output, oldSha256, source);
                }

                var described = Describe(manifestFolder, sha256, kind, file, source);
                var (manifest, status, derivativeProblem) = AddDerivatives(output, file, described, video, cancellationToken);
                Write(ManifestPath(manifestFolder, sha256), manifest);
                state.Save(new FileState(file.Path, file.Size, modifiedTicks, sha256, status));

                switch (status)
                {
                    case FileStatus.Done:
                        processed++;
                        break;
                    case FileStatus.Pending:
                        pending++;
                        break;
                    default:
                        // The manifest is kept so the file is still catalogued; the next run tries the derivatives again.
                        problems.Add(new ScanProblem(file.Path, derivativeProblem!));
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One damaged file must not stop a whole drive; it is retried on the next run.
                problems.Add(new ScanProblem(file.Path, ex.Message));
                state.Save(new FileState(file.Path, file.Size, modifiedTicks, previous?.Sha256, FileStatus.Failed));
            }

            filesDone?.Report(processed + problems.Count);
        }

        var warning = pending > 0
            ? $"FFmpeg was not found {(options.FfmpegFolder is null ? "on the PATH" : $"in {options.FfmpegFolder}")}. "
              + $"{pending:N0} videos are described but have no poster or 720p version yet; run process again once FFmpeg is installed."
            : null;
        return new ProcessResult(root, manifestFolder, stopwatch.Elapsed, processed, skipped, problems, pending, warning);
    }

    private static Manifest Describe(string manifestFolder, string sha256, MediaKind kind, FoundFile file, ManifestSource source)
    {
        var path = ManifestPath(manifestFolder, sha256);
        if (File.Exists(path))
        {
            // Same bytes seen at another path: the description is unchanged, only the list of sources grows.
            var existing = ManifestJson.Deserialize(File.ReadAllText(path));
            return existing with { Sources = WithSource(existing.Sources, source) };
        }

        var metadata = MetadataReader.Read(file.Path);
        return new Manifest
        {
            Id = sha256,
            Kind = kind,
            SizeBytes = file.Size,
            ContentType = MediaTypes.ContentType(file.Path),
            CapturedAt = metadata.CapturedAt,
            Location = metadata.Location,
            Camera = metadata.Camera,
            Width = metadata.Width,
            Height = metadata.Height,
            DurationSeconds = metadata.DurationSeconds,
            Sources = [source],
        };
    }

    /// <summary>Generates the derivatives the manifest is still missing. Problem is set when the status is Failed.</summary>
    private static (Manifest Manifest, FileStatus Status, string? Problem) AddDerivatives(
        string output, FoundFile file, Manifest manifest, VideoProcessor? video, CancellationToken cancellationToken)
    {
        if (manifest.Derivatives.Count > 0)
        {
            return (manifest, FileStatus.Done, null);
        }

        var folder = DerivativeFolder(manifest.Id);
        try
        {
            if (manifest.Kind == MediaKind.Photo)
            {
                var thumbnails = ThumbnailGenerator.Generate(file.Path, output, folder, ThumbnailGenerator.PhotoSizes);
                return (manifest with { Derivatives = thumbnails }, FileStatus.Done, null);
            }

            if (video is null)
            {
                return (manifest, FileStatus.Pending, null);
            }

            var probe = video.Probe(file.Path, cancellationToken);
            var derivatives = video.Generate(file.Path, output, folder, probe, cancellationToken);
            return (manifest with
            {
                Width = probe.Width,
                Height = probe.Height,
                DurationSeconds = probe.DurationSeconds ?? manifest.DurationSeconds,
                Derivatives = derivatives,
            }, FileStatus.Done, null);
        }
        catch (Exception ex) when (ex is MagickException or VideoProcessingException)
        {
            var what = manifest.Kind == MediaKind.Photo ? "thumbnails" : "the poster and 720p version";
            return (manifest, FileStatus.Failed, $"Could not make {what}: {ex.Message}");
        }
    }

    private static void RemoveSource(string output, string sha256, ManifestSource source)
    {
        var path = ManifestPath(Path.Combine(output, "manifests"), sha256);
        if (!File.Exists(path))
        {
            return;
        }

        var manifest = ManifestJson.Deserialize(File.ReadAllText(path));
        var remaining = manifest.Sources.Where(existing => !existing.IsSameFile(source)).ToList();
        if (remaining.Count == 0)
        {
            File.Delete(path);
            var derivatives = Path.Combine(output, DerivativeFolder(sha256));
            if (Directory.Exists(derivatives))
            {
                Directory.Delete(derivatives, recursive: true);
            }
        }
        else
        {
            Write(path, manifest with { Sources = remaining });
        }
    }

    private static List<ManifestSource> WithSource(IReadOnlyList<ManifestSource> sources, ManifestSource source) =>
        [.. sources.Where(existing => !existing.IsSameFile(source)).Append(source).OrderBy(s => s.Path, StringComparer.Ordinal)];

    private static string ManifestPath(string manifestFolder, string sha256) => Path.Combine(manifestFolder, sha256 + ".json");

    private static string DerivativeFolder(string sha256) => $"derivatives/{sha256}";

    // Written to a temporary file first so an interrupted run never leaves half a manifest.
    private static void Write(string path, Manifest manifest)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, ManifestJson.Serialize(manifest));
        File.Move(temporary, path, overwrite: true);
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
