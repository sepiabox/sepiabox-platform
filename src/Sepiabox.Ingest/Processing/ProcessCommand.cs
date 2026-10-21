using System.Diagnostics;
using ImageMagick;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Processing;
using Sepiabox.Pipeline.Video;
using Sepiabox.Search.Models;

namespace Sepiabox.Ingest.Processing;

/// <summary>
/// What a process run did. <see cref="Problems"/> are files that could not be described and make the exit code
/// non-zero. <see cref="EmbeddingProblems"/> are files that were processed but have no embedding because it could
/// not be computed: warnings, not failures, since an embedding is optional and the next run tries again.
/// <see cref="Embedded"/> counts the embeddings added in this run, to new or unchanged files. With <c>--faces</c>,
/// <see cref="FacesChecked"/> counts the files checked for faces in this run, <see cref="FacesFound"/> the faces found
/// in them, and <see cref="FaceProblems"/> the files that could not be checked, which are warnings too.
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
    IReadOnlyList<LeftOutFile>? LeftOut = null,
    int FacesChecked = 0,
    int FacesFound = 0,
    IReadOnlyList<EmbeddingProblem>? FaceProblems = null)
{
    public IReadOnlyList<EmbeddingProblem> EmbeddingProblems { get; init; } = EmbeddingProblems ?? [];

    public IReadOnlyList<EmbeddingProblem> FaceProblems { get; init; } = FaceProblems ?? [];

    /// <summary>Files with a video extension that hold no video, found in this run or earlier; they are not catalogued.</summary>
    public IReadOnlyList<LeftOutFile> LeftOut { get; init; } = LeftOut ?? [];

    public int NotVideo => LeftOut.Count;
}

/// <summary>
/// Describes every photo and video in a folder tree as one manifest per distinct content (D-005, D-014),
/// with the thumbnails, posters and 720p versions the web shows instead of the originals (D-015), and a
/// CLIP embedding computed from those thumbnails (S-015) and, only with <c>--faces</c>, the faces in them (S-018).
/// Source files are only read; results go to the work folder.
/// </summary>
public static class ProcessCommand
{
    public const string LocalSourceType = "local";

    /// <summary>
    /// Processes <see cref="ProcessOptions.Root"/> with the CLIP model when it is downloaded, and the face models when
    /// <see cref="ProcessOptions.Faces"/> is set. <paramref name="filesDone"/>
    /// is called after each photo or video, processed or not, so a long run over unchanged files shows activity.
    /// </summary>
    public static ProcessResult Run(ProcessOptions options, IProgress<int>? filesDone = null, CancellationToken cancellationToken = default)
    {
        // A wrong folder fails before the model is checked, which can take seconds the first time.
        var (root, output) = Resolve(options);
        var models = options.ModelFolder ?? ModelFolder.Default();
        var threads = ThreadsFor(options.EffectiveJobs);
        using var embedder = ClipImageEmbedder.TryCreate(models, out var embedderWarning, threads);
        string? facesWarning = null;
        using var faces = options.Faces ? FaceDetector.TryCreate(models, out facesWarning, threads) : null;
        // Each job resizes with its share of the processors, rather than every job with all of them (D-078); the
        // pixels are the same whatever the thread count. Put back afterwards, for whatever runs next in this process.
        var magickThreads = ResourceLimits.Thread;
        if (threads is not null)
        {
            ResourceLimits.Thread = (ulong)threads.IntraOpThreads;
        }

        try
        {
            return Run(
                options, root, output, embedder, embedderWarning, faces, facesWarning, TimeProvider.System, filesDone, null, cancellationToken);
        }
        finally
        {
            ResourceLimits.Thread = magickThreads;
        }
    }

    /// <summary>
    /// With more than one job, each model session and each Magick step gets its share of the processors, and ONNX
    /// Runtime's threads do not spin while they wait, which only burns the processors the other jobs need (D-078).
    /// </summary>
    internal static ModelThreads? ThreadsFor(int jobs) =>
        jobs > 1 ? new ModelThreads(Math.Max(1, Environment.ProcessorCount / jobs), AllowSpinning: false) : null;

    /// <summary>
    /// The same with a given embedder, or none; <paramref name="embedderWarning"/> says why there is none.
    /// <paramref name="clock"/> gives "now" for the plausibility of capture dates (D-046); the system clock when null.
    /// <paramref name="faces"/> is used only when <see cref="ProcessOptions.Faces"/> is set, so the opt-in is decided
    /// here and nowhere else; <paramref name="facesWarning"/> says why there is no detector.
    /// </summary>
    internal static ProcessResult Run(
        ProcessOptions options,
        IImageEmbedder? embedder,
        string? embedderWarning,
        IProgress<int>? filesDone = null,
        TimeProvider? clock = null,
        IFaceDetector? faces = null,
        string? facesWarning = null,
        ProcessHooks? hooks = null,
        CancellationToken cancellationToken = default)
    {
        var (root, output) = Resolve(options);
        return Run(
            options, root, output, embedder, embedderWarning, faces, facesWarning, clock ?? TimeProvider.System, filesDone, hooks,
            cancellationToken);
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
        IFaceDetector? faceDetector,
        string? facesWarning,
        TimeProvider clock,
        IProgress<int>? filesDone,
        ProcessHooks? hooks,
        CancellationToken cancellationToken)
    {
        // Faces are biometric data: without --faces nothing looks for them, whatever detector was passed in.
        var faces = options.Faces ? faceDetector : null;
        var manifestFolder = Path.Combine(output, "manifests");
        Directory.CreateDirectory(manifestFolder);
        DeleteAbandonedManifests(manifestFolder);

        // A drive root such as "E:\" has no folder name to fall back on.
        var sourceName = options.SourceName ?? NullIfEmpty(Path.GetFileName(Path.TrimEndingDirectorySeparator(root))) ?? root;
        var outputPrefix = Path.TrimEndingDirectorySeparator(output) + Path.DirectorySeparatorChar;

        var ffmpeg = FfmpegTools.Find(options.FfmpegFolder);
        var video = hooks?.Video ?? (ffmpeg is null ? null : new VideoProcessor(ffmpeg, options.Encoder));

        using var state = new StateStore(Path.Combine(output, "state.db"));
        var stopwatch = Stopwatch.StartNew();
        var jobs = options.EffectiveJobs;
        // A file's outcome, in the order the files were found, so the result lists come out as a sequential run's.
        var outcomes = new List<FileOutcome>();

        // An embedding or faces never decide a file's status: whatever goes wrong is a warning for that file, and the
        // next run tries again from the work folder.
        const string EmbeddingFailed = "Could not compute the embedding";
        const string FacesFailed = "Could not detect faces";
        const string UpdateFailed = "Could not update its manifest";

        // For a new file: Added is counted by the caller once the manifest is written.
        EmbeddingOutcome? AddEmbedding(FileOutcome o, string sourcePath, string assetId, Func<EmbeddingOutcome> add) =>
            Record(o, sourcePath, assetId, Try(sourcePath, o.EmbeddingProblems, EmbeddingFailed, add), o.EmbeddingProblems, o.WithoutPreviewForEmbedding);

        EmbeddingOutcome? AddFaces(FileOutcome o, string sourcePath, string assetId, Func<EmbeddingOutcome> add) =>
            Record(o, sourcePath, assetId, Try(sourcePath, o.FaceProblems, FacesFailed, add), o.FaceProblems, o.WithoutPreviewForFaces);

        // An unchanged file: its manifest is read once, both are added in memory and it is written once.
        void AddToUnchanged(FileOutcome o, string sourcePath, string assetId)
        {
            var path = ManifestPath(manifestFolder, assetId);
            // Neither step in particular failed when this throws, so the message names the manifest, not a step.
            var outcome = Try(
                sourcePath, embedder is not null ? o.EmbeddingProblems : o.FaceProblems, UpdateFailed,
                () => WorkFolderManifest.TryAdd(path, output, embedder, faces));
            Record(o, sourcePath, assetId, outcome?.Embedding, o.EmbeddingProblems, o.WithoutPreviewForEmbedding);
            o.Embedded += outcome?.Embedding?.Status == EmbeddingStatus.Added ? 1 : 0;
            CountFaces(o, Record(o, sourcePath, assetId, outcome?.Faces, o.FaceProblems, o.WithoutPreviewForFaces));
        }

        T? Try<T>(string sourcePath, List<EmbeddingProblem> optionalProblems, string what, Func<T> add)
            where T : class
        {
            try
            {
                return add();
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                optionalProblems.Add(new EmbeddingProblem(sourcePath, $"{what} ({ex.GetType().Name})", $"{what}: {ex.Message}"));
                return null;
            }
        }

        EmbeddingOutcome? Record(
            FileOutcome o, string sourcePath, string assetId, EmbeddingOutcome? outcome, List<EmbeddingProblem> optionalProblems,
            HashSet<string> withoutPreview)
        {
            switch (outcome?.Status)
            {
                case EmbeddingStatus.NoDerivative:
                    withoutPreview.Add(assetId);
                    break;
                case EmbeddingStatus.ManifestMissing:
                    o.WithoutManifest.Add(assetId);
                    break;
                case EmbeddingStatus.Failed:
                    optionalProblems.Add(new EmbeddingProblem(sourcePath, outcome.Category!, outcome.Problem!));
                    break;
            }

            return outcome;
        }

        static void CountFaces(FileOutcome o, EmbeddingOutcome? outcome)
        {
            if (outcome?.Status == EmbeddingStatus.Added)
            {
                o.FacesChecked++;
                o.FacesFound += outcome.Manifest!.Faces.Count;
            }
        }

        // The state store is one SQLite connection: every read and write goes through this lock.
        FileState? GetState(string path)
        {
            lock (state)
            {
                return state.Get(path);
            }
        }

        void SaveState(FileState fileState)
        {
            lock (state)
            {
                state.Save(fileState);
            }
        }

        // A bounded pipeline (D-078). This thread finds the files, reads each one's state and, in the order the files
        // were found, reads each photo to describe into memory (up to ReadAheadBytes waiting) or hashes a video (or a
        // photo too large to hold) as a stream: the drive, often a USB hard disk, is read front to back by this one
        // reader, and a photo only once, for its hash, its metadata and its thumbnails. Each file then goes to a task
        // that waits its turn for its content: files are registered by content id in the order they were found, and a
        // file waits for the files found before it with the same content (or the content it had before), so copies of
        // the same bytes are described exactly as a sequential run describes them. At most `jobs` files are processed
        // at once, and at most VideoJobs of them are videos (or large photos), which read the drive again to be
        // described and transcoded; a file waits for that turn before it takes a job slot, so a long transcode never
        // holds up photos that are ready. At most 4 × jobs files are in flight.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var inFlight = new SemaphoreSlim(jobs * 4);
        using var working = new SemaphoreSlim(jobs);
        using var videoReads = new SemaphoreSlim(options.EffectiveVideoJobs);
        var readAheadLimit = hooks?.ReadAheadBytes ?? ReadAheadBytes;
        var readAhead = new ByteBudget(readAheadLimit);
        var tails = new Dictionary<string, Task>(StringComparer.Ordinal);
        var tasks = new List<Task>();
        var progressLock = new object();
        var handled = 0;
        var previousRegistration = Task.CompletedTask;

        try
        {
            foreach (var file in FileTree.Enumerate(root))
            {
                if (stop.IsCancellationRequested)
                {
                    break;
                }

                var kind = MediaTypes.Classify(file.Path);
                if (kind == MediaKind.Other || file.Path.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    inFlight.Wait(stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var outcome = new FileOutcome();
                outcomes.Add(outcome);
                var after = previousRegistration;
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                previousRegistration = registered.Task;
                Plan? plan;
                try
                {
                    hooks?.BeforeRead?.Invoke(file.Path);
                    plan = Decide(file, kind, outcome);
                }
                catch (Exception ex)
                {
                    registered.SetResult();
                    inFlight.Release();
                    if (ex is OperationCanceledException && stop.IsCancellationRequested)
                    {
                        // Stopped while waiting to read: by the user, or by a failure reported below.
                        break;
                    }

                    throw;
                }

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await HandleAsync(plan, outcome, after, registered);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Not one file's problem (the state store, the work folder): the run stops, as a sequential one would.
                        await stop.CancelAsync();
                        throw;
                    }
                    finally
                    {
                        readAhead.Release(plan?.Content?.Length ?? 0);
                        inFlight.Release();
                        // Under a lock, so the count only goes up and the progress line is written by one thread at a time.
                        lock (progressLock)
                        {
                            filesDone?.Report(++handled);
                        }
                    }
                }, CancellationToken.None));
            }
        }
        finally
        {
            // Every task finishes before the semaphores and the state store go, whatever stopped the loop.
            try
            {
                Task.WaitAll([.. tasks], CancellationToken.None);
            }
            catch (AggregateException)
            {
                // Rethrown below, the first real failure first.
            }
        }

        if (tasks.Select(t => t.Exception?.InnerException).FirstOrDefault(e => e is not null and not OperationCanceledException) is { } failure)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        cancellationToken.ThrowIfCancellationRequested();

        // On the finding thread: what the file needs, from its state, and the photo's bytes when it is to be described.
        Plan? Decide(FoundFile file, MediaKind kind, FileOutcome o)
        {
            var modifiedTicks = file.ModifiedAt.UtcTicks;
            var previous = GetState(file.Path);
            var unchanged = previous is not null && previous.Size == file.Size && previous.ModifiedTicks == modifiedTicks;
            // A file whose manifest is gone is described again, or it would never be uploaded or catalogued.
            if (unchanged && previous!.Status == FileStatus.Done
                && previous.Sha256 is { } doneSha256 && File.Exists(ManifestPath(manifestFolder, doneSha256)))
            {
                o.Skipped++;
                // The model may have been downloaded, or --faces given, after this file was processed: both come
                // from the thumbnails in the work folder, so the drive is not read again.
                return embedder is not null || faces is not null
                    ? new Plan(file, kind, previous, null, null, doneSha256)
                    : null;
            }

            // Nothing about an unchanged file can make a video stream appear, short of a newer FFmpeg: that is
            // what --retry-left-out is for. A row with no reason was written under earlier, weaker rules, so it
            // is read again once.
            if (unchanged && previous!.Status == FileStatus.NotVideo && previous.Reason is not null && !options.RetryLeftOut)
            {
                o.LeftOut.Add(new LeftOutFile(file.Path, previous.Reason, Earlier: true));
                return null;
            }

            // Still no FFmpeg: re-reading a large unchanged video would change nothing.
            if (unchanged && previous!.Status == FileStatus.Pending && video is null)
            {
                o.Pending++;
                return null;
            }

            // A video, or a photo larger than the whole read-ahead (a huge TIFF or PSB), is hashed here, streamed, so
            // the drive is still hashed by this one reader in order, and read again in its task, streamed as before,
            // never into one array. Hashing it here also means a long transcode never holds up the files after it.
            if (kind != MediaKind.Photo || file.Size > readAheadLimit)
            {
                try
                {
                    return new Plan(file, kind, previous, null, null, null, MediaFile.Sha256Hex(file.Path));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return new Plan(file, kind, previous, null, ex, null);
                }
            }

            // Waits while the photos already in memory and this one would pass the read-ahead budget.
            readAhead.Wait(file.Size, stop.Token);
            try
            {
                var content = ReadAll(file.Path);
                readAhead.Adjust(file.Size, content.Length);
                return new Plan(file, kind, previous, content, null, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                readAhead.Release(file.Size);
                return new Plan(file, kind, previous, null, ex, null);
            }
        }

        async Task HandleAsync(Plan? plan, FileOutcome o, Task after, TaskCompletionSource registered)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                IReadOnlyList<string> keys = [];
                Action? work = null;
                var waitFor = Array.Empty<Task>();
                try
                {
                    if (plan is not null)
                    {
                        (keys, work) = Prepare(plan, o);
                    }
                }
                finally
                {
                    // Registered in the order the files were found, even when preparing failed, so no later file waits forever.
                    await after;
                    if (keys.Count > 0)
                    {
                        lock (tails)
                        {
                            waitFor = [.. keys.Select(k => tails.GetValueOrDefault(k)).OfType<Task>()];
                            foreach (var key in keys)
                            {
                                tails[key] = done.Task;
                            }
                        }
                    }

                    registered.SetResult();
                }

                if (work is not null)
                {
                    // The earlier files with this content finish first; their tasks never fail (done is always set).
                    await Task.WhenAll(waitFor);
                    if (stop.IsCancellationRequested)
                    {
                        return;
                    }

                    // A video, or a photo too large to read ahead, is read from the drive while it is described, probed
                    // and transcoded. Its turn for that comes first: a job slot held while waiting for it would stop
                    // photos that are ready.
                    var streamed = plan!.Content is null && plan.UnchangedSha256 is null;
                    if (streamed)
                    {
                        await videoReads.WaitAsync(stop.Token);
                    }

                    try
                    {
                        await working.WaitAsync(stop.Token);
                        try
                        {
                            hooks?.BeforeWork?.Invoke(plan.File.Path);
                            work();
                        }
                        finally
                        {
                            working.Release();
                        }
                    }
                    finally
                    {
                        if (streamed)
                        {
                            videoReads.Release();
                        }
                    }
                }
            }
            finally
            {
                done.SetResult();
            }
        }

        // In the task: the file's hash (from memory for a photo; a streamed file was hashed by the reader), then the
        // content ids it touches and the work to do on them.
        (IReadOnlyList<string> Keys, Action? Work) Prepare(Plan plan, FileOutcome o)
        {
            var file = plan.File;
            if (plan.UnchangedSha256 is { } doneSha256)
            {
                return ([doneSha256], () => AddToUnchanged(o, file.Path, doneSha256));
            }

            string sha256;
            try
            {
                if (plan.ReadError is { } readError)
                {
                    throw readError;
                }

                sha256 = plan.Content is { } content ? MediaFile.Sha256Hex(content) : plan.StreamedSha256!;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One damaged file must not stop a whole drive; it is retried on the next run.
                o.Problems.Add(new ScanProblem(file.Path, ex.Message));
                SaveState(new FileState(file.Path, file.Size, file.ModifiedAt.UtcTicks, plan.Previous?.Sha256, FileStatus.Failed));
                return ([], null);
            }

            // A changed file also touches the content it had before, whose manifest loses this path.
            List<string> keys = plan.Previous?.Sha256 is { } before && before != sha256 ? [sha256, before] : [sha256];
            return (keys, () => ProcessFile(file, plan.Kind, o, plan.Previous, sha256, plan.Content));
        }

        void ProcessFile(FoundFile file, MediaKind kind, FileOutcome o, FileState? previous, string sha256, byte[]? content)
        {
            var modifiedTicks = file.ModifiedAt.UtcTicks;
            try
            {
                var source = new ManifestSource(
                    LocalSourceType,
                    sourceName,
                    ManifestSource.NormalizePath(Path.GetRelativePath(root, file.Path)),
                    file.ModifiedAt);

                if (previous?.Sha256 is { } oldSha256 && oldSha256 != sha256)
                {
                    RemoveSource(output, oldSha256, source);
                }

                var described = Describe(manifestFolder, sha256, kind, file, source, clock, content);
                var (manifest, status, derivativeProblem, noVideo) = AddDerivatives(output, file, described, content, video, stop.Token);
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
                        var outcome = AddEmbedding(o, file.Path, sha256, () => ManifestEmbeddings.TryAdd(withDerivatives, output, embedder));
                        embeddingAdded = outcome?.Status == EmbeddingStatus.Added;
                        manifest = outcome?.Manifest ?? manifest;
                    }

                    EmbeddingOutcome? facesOutcome = null;
                    if (faces is not null && status == FileStatus.Done)
                    {
                        var withDerivatives = manifest;
                        facesOutcome = AddFaces(o, file.Path, sha256, () => ManifestFaces.TryAdd(withDerivatives, output, faces));
                        manifest = facesOutcome?.Manifest ?? manifest;
                    }

                    ManifestJson.WriteFile(ManifestPath(manifestFolder, sha256), manifest);
                    o.Embedded += embeddingAdded ? 1 : 0;
                    CountFaces(o, facesOutcome);
                }

                // NotVideo is remembered too, with its reason, so an unchanged file is not read again.
                SaveState(new FileState(file.Path, file.Size, modifiedTicks, sha256, status, noVideo));

                switch (status)
                {
                    case FileStatus.Done:
                        o.Processed++;
                        break;
                    case FileStatus.Pending:
                        o.Pending++;
                        break;
                    case FileStatus.NotVideo:
                        o.LeftOut.Add(new LeftOutFile(file.Path, noVideo, Earlier: false));
                        break;
                    default:
                        // The manifest is kept so the file is still catalogued; the next run tries the derivatives again.
                        o.Problems.Add(new ScanProblem(file.Path, derivativeProblem ?? "Could not make its previews."));
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One damaged file must not stop a whole drive; it is retried on the next run.
                o.Problems.Add(new ScanProblem(file.Path, ex.Message));
                SaveState(new FileState(file.Path, file.Size, modifiedTicks, previous?.Sha256, FileStatus.Failed));
            }
        }

        // The files' outcomes, in the order they were found.
        var problems = outcomes.SelectMany(o => o.Problems).ToList();
        var embeddingProblems = outcomes.SelectMany(o => o.EmbeddingProblems).ToList();
        var faceProblems = outcomes.SelectMany(o => o.FaceProblems).ToList();
        var leftOut = outcomes.SelectMany(o => o.LeftOut).ToList();
        int processed = outcomes.Sum(o => o.Processed), skipped = outcomes.Sum(o => o.Skipped), pending = outcomes.Sum(o => o.Pending);
        int embedded = outcomes.Sum(o => o.Embedded), facesChecked = outcomes.Sum(o => o.FacesChecked), facesFound = outcomes.Sum(o => o.FacesFound);
        // By asset, so the same photo in two folders counts once.
        var withoutPreviewForEmbedding = outcomes.SelectMany(o => o.WithoutPreviewForEmbedding).ToHashSet(StringComparer.Ordinal);
        var withoutPreviewForFaces = outcomes.SelectMany(o => o.WithoutPreviewForFaces).ToHashSet(StringComparer.Ordinal);
        var withoutManifest = outcomes.SelectMany(o => o.WithoutManifest).ToHashSet(StringComparer.Ordinal);

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

        if (options.Faces && faces is null && facesWarning is not null)
        {
            warnings.Add(facesWarning);
        }

        // One line each for all of them: a work folder whose files were deleted would otherwise list every file.
        var withoutPreview = withoutPreviewForEmbedding.Union(withoutPreviewForFaces).Count();
        if (withoutPreview > 0)
        {
            var missing = (withoutPreviewForEmbedding.Count > 0, withoutPreviewForFaces.Count > 0) switch
            {
                (true, true) => "have no embedding or were not checked for faces",
                (false, true) => "were not checked for faces",
                _ => "have no embedding",
            };
            warnings.Add(
                $"{withoutPreview:N0} photos and videos have no preview or poster in the work folder, so they {missing}. "
                + "Keep the work folder until they are processed.");
        }

        if (withoutManifest.Count > 0)
        {
            // Only when a manifest disappears during the run: the next run describes those files again.
            warnings.Add($"{withoutManifest.Count:N0} manifests disappeared from {manifestFolder} during the run; the next run describes them again.");
        }

        var warning = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
        return new ProcessResult(
            root, manifestFolder, stopwatch.Elapsed, processed, skipped, problems, pending, warning, embedded, embeddingProblems, leftOut,
            facesChecked, facesFound, faceProblems);
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
        string manifestFolder, string sha256, MediaKind kind, FoundFile file, ManifestSource source, TimeProvider clock, byte[]? content)
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

        MediaMetadata metadata;
        if (content is null)
        {
            metadata = MetadataReader.Read(file.Path);
        }
        else
        {
            using var stream = new MemoryStream(content, writable: false);
            metadata = MetadataReader.Read(stream);
        }

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
        string output, FoundFile file, Manifest manifest, byte[]? content, IVideoProcessor? video, CancellationToken cancellationToken)
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
                IReadOnlyList<ManifestDerivative> thumbnails;
                if (content is null)
                {
                    thumbnails = ThumbnailGenerator.Generate(file.Path, output, folder, ThumbnailGenerator.PhotoSizes);
                }
                else
                {
                    using var stream = new MemoryStream(content, writable: false);
                    thumbnails = ThumbnailGenerator.Generate(stream, output, folder, ThumbnailGenerator.PhotoSizes);
                }

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
                // MetadataExtractor misses some containers' positions (an MP4's loci box), which ffprobe reads.
                Location = manifest.Location ?? probe.Location,
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

    /// <summary>
    /// Test seams: a stand-in for FFmpeg, a call before the reader reads a file, another before its work starts (after
    /// its turn came), and another read-ahead budget.
    /// </summary>
    internal sealed record ProcessHooks(
        IVideoProcessor? Video = null, Action<string>? BeforeWork = null, long? ReadAheadBytes = null, Action<string>? BeforeRead = null);

    /// <summary>Photos read ahead and waiting in memory at most, so a slow step never lets the reader fill the memory.</summary>
    internal const long ReadAheadBytes = 256L * 1024 * 1024;

    // Read-only, like every read of the drive, in one pass from the start.
    private static byte[] ReadAll(string path)
    {
        using var stream = MediaFile.OpenRead(path);
        var length = stream.Length;
        if (length > Array.MaxLength)
        {
            throw new IOException($"The photo is too large to read ({length:N0} bytes).");
        }

        var content = new byte[length];
        stream.ReadExactly(content);
        // A file that grew while it was read is read as it was found; the hash is of these bytes.
        return content;
    }

    /// <summary>
    /// A file to process and what is known of it before its task runs: its state, the photo's bytes (or why they could
    /// not be read), or the content id of an unchanged file that only needs its embedding or faces.
    /// </summary>
    private sealed record Plan(
        FoundFile File,
        MediaKind Kind,
        FileState? Previous,
        byte[]? Content,
        Exception? ReadError,
        string? UnchangedSha256,
        string? StreamedSha256 = null);

    /// <summary>What one file added to the run's result, kept apart so the result is gathered in the order files were found.</summary>
    private sealed class FileOutcome
    {
        public int Processed { get; set; }

        public int Skipped { get; set; }

        public int Pending { get; set; }

        public int Embedded { get; set; }

        public int FacesChecked { get; set; }

        public int FacesFound { get; set; }

        public List<ScanProblem> Problems { get; } = [];

        public List<EmbeddingProblem> EmbeddingProblems { get; } = [];

        public List<EmbeddingProblem> FaceProblems { get; } = [];

        public List<LeftOutFile> LeftOut { get; } = [];

        public HashSet<string> WithoutPreviewForEmbedding { get; } = new(StringComparer.Ordinal);

        public HashSet<string> WithoutPreviewForFaces { get; } = new(StringComparer.Ordinal);

        public HashSet<string> WithoutManifest { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Bytes of photos in memory: the reader waits while the budget is spent. Larger photos never come here: they are
    /// streamed.
    /// </summary>
    private sealed class ByteBudget(long limit)
    {
        private readonly object _lock = new();
        private long _held;

        public void Wait(long bytes, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                while (_held > 0 && _held + bytes > limit)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Woken by every release; the timeout only lets a cancellation through.
                    Monitor.Wait(_lock, TimeSpan.FromMilliseconds(200));
                }

                _held += bytes;
            }
        }

        // The file's size when found and its length when read can differ when it changes meanwhile.
        public void Adjust(long expected, long actual) => Release(expected - actual);

        public void Release(long bytes)
        {
            if (bytes == 0)
            {
                return;
            }

            lock (_lock)
            {
                _held -= bytes;
                Monitor.PulseAll(_lock);
            }
        }
    }
}
