using System.Diagnostics;
using ImageMagick;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Models;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Processing;

/// <summary>
/// What a process run did. <see cref="Problems"/> are files that could not be described and make the exit code
/// non-zero. <see cref="EmbeddingProblems"/> are files that were processed but have no embedding because it could
/// not be computed: warnings, not failures, since an embedding is optional and the next run tries again.
/// <see cref="Embedded"/> counts the embeddings added in this run, to new or unchanged files.
/// </summary>
public sealed record ProcessResult(
    string Root,
    string ManifestFolder,
    TimeSpan Duration,
    int Processed,
    int Skipped,
    IReadOnlyList<ScanProblem> Problems,
    int VideosPending = 0,
    string? Warning = null,
    int Embedded = 0,
    IReadOnlyList<EmbeddingProblem>? EmbeddingProblems = null,
    IReadOnlyList<LeftOutFile>? LeftOut = null)
{
    public IReadOnlyList<EmbeddingProblem> EmbeddingProblems { get; init; } = EmbeddingProblems ?? [];

    /// <summary>Files with a video extension that hold no video, found in this run or earlier; they are not catalogued.</summary>
    public IReadOnlyList<LeftOutFile> LeftOut { get; init; } = LeftOut ?? [];

    public int NotVideo => LeftOut.Count;
}

/// <summary>
/// Describes every photo and video in a folder tree as one manifest per distinct content (D-005, D-014),
/// with the thumbnails, posters and 720p versions the web shows instead of the originals (D-015), and a
/// CLIP embedding computed from those thumbnails (S-015). Source files are only read; results go to the
/// work folder.
/// </summary>
public static class ProcessCommand
{
    public const string LocalSourceType = "local";

    /// <summary>
    /// Processes <see cref="ProcessOptions.Root"/> with the CLIP model when it is downloaded. <paramref name="filesDone"/>
    /// is called after each photo or video, processed or not, so a long run over unchanged files shows activity.
    /// </summary>
    public static ProcessResult Run(ProcessOptions options, IProgress<int>? filesDone = null, CancellationToken cancellationToken = default)
    {
        // A wrong folder fails before the model is checked, which can take seconds the first time.
        var (root, output) = Resolve(options);
        using var embedder = ClipImageEmbedder.TryCreate(options.ModelFolder ?? ModelFolder.Default(), out var embedderWarning);
        return Run(options, root, output, embedder, embedderWarning, TimeProvider.System, filesDone, cancellationToken);
    }

    /// <summary>
    /// The same with a given embedder, or none; <paramref name="embedderWarning"/> says why there is none.
    /// <paramref name="clock"/> gives "now" for the plausibility of capture dates (D-046); the system clock when null.
    /// </summary>
    internal static ProcessResult Run(
        ProcessOptions options,
        IImageEmbedder? embedder,
        string? embedderWarning,
        IProgress<int>? filesDone = null,
        TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        var (root, output) = Resolve(options);
        return Run(options, root, output, embedder, embedderWarning, clock ?? TimeProvider.System, filesDone, cancellationToken);
    }

    /// <summary>The folder to read, which must exist, and the work folder, both as full paths.</summary>
    private static (string Root, string Output) Resolve(ProcessOptions options) =>
        (FileTree.ResolveRoot(options.Root), Path.GetFullPath(options.OutputPath));

    private static ProcessResult Run(
        ProcessOptions options,
        string root,
        string output,
        IImageEmbedder? embedder,
        string? embedderWarning,
        TimeProvider clock,
        IProgress<int>? filesDone,
        CancellationToken cancellationToken)
    {
        var manifestFolder = Path.Combine(output, "manifests");
        Directory.CreateDirectory(manifestFolder);
        DeleteAbandonedManifests(manifestFolder);

        // A drive root such as "E:\" has no folder name to fall back on.
        var sourceName = options.SourceName ?? NullIfEmpty(Path.GetFileName(Path.TrimEndingDirectorySeparator(root))) ?? root;
        var outputPrefix = Path.TrimEndingDirectorySeparator(output) + Path.DirectorySeparatorChar;

        var ffmpeg = FfmpegTools.Find(options.FfmpegFolder);
        var video = ffmpeg is null ? null : new VideoProcessor(ffmpeg, options.Encoder);

        using var state = new StateStore(Path.Combine(output, "state.db"));
        var stopwatch = Stopwatch.StartNew();
        var problems = new List<ScanProblem>();
        var embeddingProblems = new List<EmbeddingProblem>();
        int processed = 0, skipped = 0, pending = 0, embedded = 0, handled = 0;
        var leftOut = new List<LeftOutFile>();
        // By asset, so the same photo in two folders counts once.
        var withoutDerivative = new HashSet<string>(StringComparer.Ordinal);
        var withoutManifest = new HashSet<string>(StringComparer.Ordinal);

        // An embedding never decides a file's status: whatever goes wrong is a warning for that file, and the
        // next run tries again from the work folder.
        // Added is counted by the caller once the manifest is written.
        EmbeddingOutcome? AddEmbedding(string sourcePath, string assetId, Func<EmbeddingOutcome> add)
        {
            try
            {
                var outcome = add();
                switch (outcome.Status)
                {
                    case EmbeddingStatus.NoDerivative:
                        withoutDerivative.Add(assetId);
                        break;
                    case EmbeddingStatus.ManifestMissing:
                        withoutManifest.Add(assetId);
                        break;
                    case EmbeddingStatus.Failed:
                        embeddingProblems.Add(new EmbeddingProblem(sourcePath, outcome.Category!, outcome.Problem!));
                        break;
                }

                return outcome;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                embeddingProblems.Add(new EmbeddingProblem(
                    sourcePath, $"Could not compute the embedding ({ex.GetType().Name})", $"Could not compute the embedding: {ex.Message}"));
                return null;
            }
        }

        foreach (var file in FileTree.Enumerate(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var kind = MediaTypes.Classify(file.Path);
            if (kind == MediaKind.Other || file.Path.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                ProcessFile(file, kind);
            }
            finally
            {
                filesDone?.Report(++handled);
            }
        }

        void ProcessFile(FoundFile file, MediaKind kind)
        {
            var modifiedTicks = file.ModifiedAt.UtcTicks;
            var previous = state.Get(file.Path);
            var unchanged = previous is not null && previous.Size == file.Size && previous.ModifiedTicks == modifiedTicks;
            // A file whose manifest is gone is described again, or it would never be uploaded or catalogued.
            if (unchanged && previous!.Status == FileStatus.Done
                && previous.Sha256 is { } doneSha256 && File.Exists(ManifestPath(manifestFolder, doneSha256)))
            {
                skipped++;
                // The model may have been downloaded after this file was processed: the embedding comes from
                // the thumbnails in the work folder, so the drive is not read again.
                if (embedder is not null)
                {
                    var outcome = AddEmbedding(file.Path, doneSha256, () => ManifestEmbeddings.TryAddToFile(ManifestPath(manifestFolder, doneSha256), output, embedder));
                    embedded += outcome?.Status == EmbeddingStatus.Added ? 1 : 0;
                }

                return;
            }

            // Nothing about an unchanged file can make a video stream appear, short of a newer FFmpeg: that is
            // what --retry-left-out is for. A row with no reason was written under earlier, weaker rules, so it
            // is read again once.
            if (unchanged && previous!.Status == FileStatus.NotVideo && previous.Reason is not null && !options.RetryLeftOut)
            {
                leftOut.Add(new LeftOutFile(file.Path, previous.Reason, Earlier: true));
                return;
            }

            // Still no FFmpeg: re-reading a large unchanged video would change nothing.
            if (unchanged && previous!.Status == FileStatus.Pending && video is null)
            {
                pending++;
                return;
            }

            try
            {
                var sha256 = MediaFile.Sha256Hex(file.Path);
                var source = new ManifestSource(
                    LocalSourceType,
                    sourceName,
                    ManifestSource.NormalizePath(Path.GetRelativePath(root, file.Path)),
                    file.ModifiedAt);

                if (previous?.Sha256 is { } oldSha256 && oldSha256 != sha256)
                {
                    RemoveSource(output, oldSha256, source);
                }

                var described = Describe(manifestFolder, sha256, kind, file, source, clock);
                var (manifest, status, derivativeProblem, noVideo) = AddDerivatives(output, file, described, video, cancellationToken);
                if (status == FileStatus.NotVideo)
                {
                    // Not catalogued. A run without FFmpeg may have written a manifest for it already: this path
                    // leaves it, and with the last path the manifest and derivatives go too.
                    RemoveSource(output, sha256, source);
                }
                else
                {
                    var embeddingAdded = false;
                    if (embedder is not null && status == FileStatus.Done)
                    {
                        var withDerivatives = manifest;
                        var outcome = AddEmbedding(file.Path, sha256, () => ManifestEmbeddings.TryAdd(withDerivatives, output, embedder));
                        embeddingAdded = outcome?.Status == EmbeddingStatus.Added;
                        manifest = outcome?.Manifest ?? manifest;
                    }

                    ManifestJson.WriteFile(ManifestPath(manifestFolder, sha256), manifest);
                    embedded += embeddingAdded ? 1 : 0;
                }

                // NotVideo is remembered too, with its reason, so an unchanged file is not read again.
                state.Save(new FileState(file.Path, file.Size, modifiedTicks, sha256, status, noVideo));

                switch (status)
                {
                    case FileStatus.Done:
                        processed++;
                        break;
                    case FileStatus.Pending:
                        pending++;
                        break;
                    case FileStatus.NotVideo:
                        leftOut.Add(new LeftOutFile(file.Path, noVideo, Earlier: false));
                        break;
                    default:
                        // The manifest is kept so the file is still catalogued; the next run tries the derivatives again.
                        problems.Add(new ScanProblem(file.Path, derivativeProblem ?? "Could not make its previews."));
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One damaged file must not stop a whole drive; it is retried on the next run.
                problems.Add(new ScanProblem(file.Path, ex.Message));
                state.Save(new FileState(file.Path, file.Size, modifiedTicks, previous?.Sha256, FileStatus.Failed));
            }
        }

        var warnings = new List<string>();
        if (pending > 0)
        {
            warnings.Add(
                $"FFmpeg was not found {(options.FfmpegFolder is null ? "on the PATH" : $"in {options.FfmpegFolder}")}. "
                + $"{pending:N0} videos are described but have no poster or 720p version yet; run process again once FFmpeg is installed.");
        }

        if (embedder is null && embedderWarning is not null)
        {
            warnings.Add(embedderWarning);
        }

        // One line each for all of them: a work folder whose files were deleted would otherwise list every file.
        if (withoutDerivative.Count > 0)
        {
            warnings.Add(
                $"{withoutDerivative.Count:N0} photos and videos have no preview or poster in the work folder, so they have no embedding. "
                + "Keep the work folder until embeddings are computed.");
        }

        if (withoutManifest.Count > 0)
        {
            // Only when a manifest disappears during the run: the next run describes those files again.
            warnings.Add($"{withoutManifest.Count:N0} manifests disappeared from {manifestFolder} during the run; the next run describes them again.");
        }

        var warning = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
        return new ProcessResult(
            root, manifestFolder, stopwatch.Elapsed, processed, skipped, problems, pending, warning, embedded, embeddingProblems, leftOut);
    }

    /// <summary>
    /// Removes temporary manifest files left by a run that was killed while writing. An hour is far longer than
    /// any write, so a run going on in parallel keeps its files.
    /// </summary>
    private static void DeleteAbandonedManifests(string manifestFolder)
    {
        foreach (var file in Directory.EnumerateFiles(manifestFolder, "*.tmp"))
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) <= TimeSpan.FromHours(1))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still open elsewhere; a later run tries again.
            }
        }
    }

    private static Manifest Describe(
        string manifestFolder, string sha256, MediaKind kind, FoundFile file, ManifestSource source, TimeProvider clock)
    {
        var path = ManifestPath(manifestFolder, sha256);
        if (File.Exists(path))
        {
            // A copy of the same bytes: the description is unchanged and the list of sources grows. Keep the stored
            // date unless this path gives a stronger one (D-046); the bytes' metadata is already in the manifest.
            var existing = ManifestJson.Deserialize(File.ReadAllText(path));
            return existing with
            {
                Captured = CaptureDate.Stronger(existing.Captured, CaptureDate.Resolve(null, file.Path, clock)),
                Sources = WithSource(existing.Sources, source),
            };
        }

        var metadata = MetadataReader.Read(file.Path);
        return new Manifest
        {
            Id = sha256,
            Kind = kind,
            SizeBytes = file.Size,
            ContentType = MediaTypes.ContentType(file.Path),
            // The file's own date when plausible, otherwise one from its path (D-046).
            Captured = CaptureDate.Resolve(metadata.RawCapturedAt, file.Path, clock),
            Location = metadata.Location,
            Camera = metadata.Camera,
            Width = metadata.Width,
            Height = metadata.Height,
            DurationSeconds = metadata.DurationSeconds,
            Sources = [source],
        };
    }

    /// <summary>
    /// Generates the derivatives the manifest is still missing. Problem is set when the status is Failed, and
    /// NoVideo when it is NotVideo.
    /// </summary>
    private static (Manifest Manifest, FileStatus Status, string? Problem, NoVideoReason? NoVideo) AddDerivatives(
        string output, FoundFile file, Manifest manifest, VideoProcessor? video, CancellationToken cancellationToken)
    {
        if (manifest.Derivatives.Count > 0)
        {
            return (manifest, FileStatus.Done, null, null);
        }

        var folder = DerivativeFolder(manifest.Id);
        try
        {
            if (manifest.Kind == MediaKind.Photo)
            {
                var thumbnails = ThumbnailGenerator.Generate(file.Path, output, folder, ThumbnailGenerator.PhotoSizes);
                return (manifest with { Derivatives = thumbnails }, FileStatus.Done, null, null);
            }

            // Checked without FFmpeg, whose module support depends on how it was built.
            if (TrackerModule.IsTrackerModule(file.Path))
            {
                return (manifest, FileStatus.NotVideo, null, NoVideoReason.TrackerModule);
            }

            if (video is null)
            {
                return (manifest, FileStatus.Pending, null, null);
            }

            var probe = video.Probe(file.Path, cancellationToken);
            var derivatives = video.Generate(file.Path, output, folder, probe, cancellationToken);
            return (manifest with
            {
                Width = probe.Width,
                Height = probe.Height,
                DurationSeconds = probe.DurationSeconds ?? manifest.DurationSeconds,
                Derivatives = derivatives,
            }, FileStatus.Done, null, null);
        }
        catch (NoVideoStreamException ex)
        {
            return (manifest, FileStatus.NotVideo, null, ex.Reason);
        }
        catch (Exception ex) when (ex is MagickException or VideoProcessingException)
        {
            var what = manifest.Kind == MediaKind.Photo ? "thumbnails" : "the poster and 720p version";
            return (manifest, FileStatus.Failed, $"Could not make {what}: {ex.Message}", null);
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
            ManifestJson.WriteFile(path, manifest with { Sources = remaining });
        }
    }

    private static List<ManifestSource> WithSource(IReadOnlyList<ManifestSource> sources, ManifestSource source) =>
        [.. sources.Where(existing => !existing.IsSameFile(source)).Append(source).OrderBy(s => s.Path, StringComparer.Ordinal)];

    private static string ManifestPath(string manifestFolder, string sha256) => Path.Combine(manifestFolder, sha256 + ".json");

    private static string DerivativeFolder(string sha256) => $"derivatives/{sha256}";

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
