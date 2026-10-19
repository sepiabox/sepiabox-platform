using System.Diagnostics;
using ImageMagick;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Processing;

public sealed record ProcessResult(
    string Root,
    string ManifestFolder,
    TimeSpan Duration,
    int Processed,
    int Skipped,
    IReadOnlyList<ScanProblem> Problems);

/// <summary>
/// Describes every photo and video in a folder tree as one manifest per distinct content (D-005, D-014),
/// with the thumbnails the web shows instead of the originals (D-015).
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

        using var state = new StateStore(Path.Combine(output, "state.db"));
        var stopwatch = Stopwatch.StartNew();
        var problems = new List<ScanProblem>();
        int processed = 0, skipped = 0;

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
            if (previous is { Status: FileStatus.Done } && previous.Size == file.Size && previous.ModifiedTicks == modifiedTicks)
            {
                skipped++;
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

                var (manifest, derivativeProblem) = AddDerivatives(output, file, Describe(manifestFolder, sha256, kind, file, source));
                Write(ManifestPath(manifestFolder, sha256), manifest);

                if (derivativeProblem is null)
                {
                    state.Save(new FileState(file.Path, file.Size, modifiedTicks, sha256, FileStatus.Done));
                    processed++;
                }
                else
                {
                    // The manifest is kept so the file is still catalogued; the next run tries the derivatives again.
                    problems.Add(new ScanProblem(file.Path, derivativeProblem));
                    state.Save(new FileState(file.Path, file.Size, modifiedTicks, sha256, FileStatus.Failed));
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

        return new ProcessResult(root, manifestFolder, stopwatch.Elapsed, processed, skipped, problems);
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

    /// <summary>Generates the derivatives the manifest is still missing. Problem is null on success.</summary>
    private static (Manifest Manifest, string? Problem) AddDerivatives(string output, FoundFile file, Manifest manifest)
    {
        if (manifest.Kind != MediaKind.Photo || manifest.Derivatives.Count > 0)
        {
            return (manifest, null);
        }

        try
        {
            var derivatives = ThumbnailGenerator.Generate(file.Path, output, DerivativeFolder(manifest.Id), ThumbnailGenerator.PhotoSizes);
            return (manifest with { Derivatives = derivatives }, null);
        }
        catch (MagickException ex)
        {
            return (manifest, $"Could not make thumbnails: {ex.Message}");
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
