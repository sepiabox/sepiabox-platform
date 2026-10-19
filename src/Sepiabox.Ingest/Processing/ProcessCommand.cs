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

public sealed record ProcessResult(
    string Root,
    string ManifestFolder,
    TimeSpan Duration,
    int Processed,
    int Skipped,
    IReadOnlyList<ScanProblem> Problems,
    int VideosPending = 0,
    string? Warning = null,
    int Embedded = 0);

/// <summary>
/// Describes every photo and video in a folder tree as one manifest per distinct content (D-005, D-014),
/// with the thumbnails, posters and 720p versions the web shows instead of the originals (D-015), and a
/// CLIP embedding computed from those thumbnails (S-015). Source files are only read; results go to the
/// work folder.
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
        var modelFolder = options.ModelFolder ?? ModelFolder.Default();
        using var embedder = ClipImageEmbedder.TryCreate(modelFolder, options.UseGpu);

        using var state = new StateStore(Path.Combine(output, "state.db"));
        var stopwatch = Stopwatch.StartNew();
        var problems = new List<ScanProblem>();
        int processed = 0, skipped = 0, pending = 0, embedded = 0;

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
                // The model may have been downloaded after this file was processed: the embedding comes from
                // the thumbnails in the work folder, so the drive is not read again.
                if (embedder is not null && previous.Sha256 is { } doneSha256)
                {
                    embedded += AddMissingEmbedding(output, ManifestPath(manifestFolder, doneSha256), file.Path, embedder, problems);
                }

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
                    ManifestSource.NormalizePath(Path.GetRelativePath(root, file.Path)),
                    file.ModifiedAt);

                if (previous?.Sha256 is { } oldSha256 && oldSha256 != sha256)
                {
                    RemoveSource(output, oldSha256, source);
                }

                var described = Describe(manifestFolder, sha256, kind, file, source);
                var (manifest, status, derivativeProblem) = AddDerivatives(output, file, described, video, cancellationToken);
                if (embedder is not null && status == FileStatus.Done)
                {
                    (manifest, var embeddingProblem) = WithEmbedding(output, manifest, embedder);
                    if (embeddingProblem is null)
                    {
                        embedded += manifest.Embeddings.ContainsKey(ClipImageEmbedder.ModelKey) ? 1 : 0;
                    }
                    else
                    {
                        // The file stays Done: the next run adds the embedding from the work folder.
                        problems.Add(new ScanProblem(file.Path, embeddingProblem));
                    }
                }

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

        var warnings = new List<string>();
        if (pending > 0)
        {
            warnings.Add(
                $"FFmpeg was not found {(options.FfmpegFolder is null ? "on the PATH" : $"in {options.FfmpegFolder}")}. "
                + $"{pending:N0} videos are described but have no poster or 720p version yet; run process again once FFmpeg is installed.");
        }

        if (embedder is null)
        {
            warnings.Add(
                $"The CLIP model is not in {modelFolder}, so no embeddings were computed. Run `sepiabox-ingest models download` "
                + "and then process again: embeddings are added from the work folder without reading the drive again.");
        }
        else if (embedder.Warning is not null)
        {
            warnings.Add(embedder.Warning);
        }

        var warning = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
        return new ProcessResult(root, manifestFolder, stopwatch.Elapsed, processed, skipped, problems, pending, warning, embedded);
    }

    /// <summary>
    /// Adds the CLIP embedding when the manifest has none, from the derivative the web shows: the 1280 px
    /// preview of a photo or the poster of a video. Problem is set when the image could not be embedded.
    /// </summary>
    private static (Manifest Manifest, string? Problem) WithEmbedding(string output, Manifest manifest, ClipImageEmbedder embedder)
    {
        if (manifest.Embeddings.ContainsKey(ClipImageEmbedder.ModelKey))
        {
            return (manifest, null);
        }

        var name = manifest.Kind == MediaKind.Video ? VideoProcessor.PosterName : ThumbnailGenerator.PreviewName;
        var derivative = manifest.Derivatives.FirstOrDefault(d => d.Name == name);
        if (derivative is null)
        {
            return (manifest, null);
        }

        try
        {
            var vector = embedder.Embed(Path.Combine(output, derivative.Path));
            var embeddings = new Dictionary<string, System.Text.Json.Nodes.JsonNode>(manifest.Embeddings.ToDictionary(e => e.Key, e => e.Value))
            {
                [ClipImageEmbedder.ModelKey] = System.Text.Json.Nodes.JsonValue.Create(EmbeddingCodec.Encode(vector)),
            };
            return (manifest with { Embeddings = embeddings }, null);
        }
        catch (Exception ex) when (ex is MagickException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or IOException)
        {
            return (manifest, $"Could not compute the CLIP embedding: {ex.Message}");
        }
    }

    private static int AddMissingEmbedding(
        string output, string manifestPath, string sourcePath, ClipImageEmbedder embedder, List<ScanProblem> problems)
    {
        if (!File.Exists(manifestPath))
        {
            return 0;
        }

        var manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));
        if (manifest.Embeddings.ContainsKey(ClipImageEmbedder.ModelKey))
        {
            return 0;
        }

        var (withEmbedding, problem) = WithEmbedding(output, manifest, embedder);
        if (problem is not null)
        {
            problems.Add(new ScanProblem(sourcePath, problem));
            return 0;
        }

        if (ReferenceEquals(withEmbedding, manifest))
        {
            return 0;
        }

        Write(manifestPath, withEmbedding);
        return 1;
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
