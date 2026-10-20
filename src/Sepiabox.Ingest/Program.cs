using Sepiabox.Ingest;
using Sepiabox.Ingest.Models;
using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Ingest.Uploading;
using Sepiabox.Pipeline.Models;

const string Usage = """
    Usage: sepiabox-ingest <command> [options]

    Commands:
      scan <folder>                  Read a folder or drive and report what it holds. Changes nothing.
      process <folder> --out <dir>   Describe every photo and video in one manifest per file content,
                                     with WebP thumbnails and, for videos, a poster and a 720p version.
      upload <dir> --destination local|azure
                                     Send a process work folder to Blob Storage: originals, then
                                     thumbnails and videos, then manifests. Running again resumes.
      models download                Download the ML models (CLIP) and check their SHA-256.

    Options for scan:
      --report <file>  Also write the full result, with every duplicate and problem, as JSON.
      --no-duplicates  Skip duplicate detection. Faster, but no duplicate count.

    Options for process:
      --out <dir>           Work folder for manifests and progress. Running again resumes.
      --source-name <name>  Name of this drive or folder, such as "HD da Ana". Default: the folder name.
      --ffmpeg <folder>     Folder with ffmpeg and ffprobe. Default: look on the PATH.
      --hw nvenc            Encode the 720p versions on an NVIDIA GPU.
      --models <folder>     Folder with the downloaded models. Default: where models download puts them.

    Options for models download:
      --models <folder>     Where to put the models. Default: %LOCALAPPDATA%\Sepiabox\models on
                            Windows, ~/.sepiabox/models elsewhere (or SEPIABOX_MODELS).

    Options for upload:
      --destination local   Azurite, or the connection string in SEPIABOX_BLOB_CONNECTION.
      --destination azure   An Azure storage account; sign in first with az login.
      --account <name>      Storage account name, for --destination azure.
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    return args[0] switch
    {
        "scan" => await RunScan(args[1..], cancellation.Token),
        "process" => RunProcess(args[1..], cancellation.Token),
        "upload" => await RunUpload(args[1..], cancellation.Token),
        "models" => await RunModels(args[1..], cancellation.Token),
        _ => UsageError($"Unknown command '{args[0]}'."),
    };
}
catch (DirectoryNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Cancelled. Running the same command again continues where it stopped.");
    return 130;
}

static async Task<int> RunScan(string[] args, CancellationToken cancellationToken)
{
    if (!ScanOptions.TryParse(args, out var options, out var error))
    {
        return UsageError(error);
    }

    var progress = new ConsoleProgress<int>(count => Console.Error.Write($"\r{count:N0} files read..."));
    var result = Scanner.Scan(options, progress, cancellationToken);
    Console.Error.WriteLine();

    ScanReport.WriteSummary(result, Console.Out);

    if (options.ReportPath is not null)
    {
        await using var file = File.Create(options.ReportPath);
        ScanReport.WriteJson(result, file);
        Console.WriteLine();
        Console.WriteLine($"Full report written to {Path.GetFullPath(options.ReportPath)}");
    }

    return 0;
}

static int RunProcess(string[] args, CancellationToken cancellationToken)
{
    if (!ProcessOptions.TryParse(args, out var options, out var error))
    {
        return UsageError(error);
    }

    var progress = new ConsoleProgress<int>(count => Console.Error.Write($"\r{count:N0} photos and videos checked..."));
    var result = ProcessCommand.Run(options, progress, cancellationToken);
    Console.Error.WriteLine();

    Console.WriteLine($"Processed {result.Processed:N0}, unchanged {result.Skipped:N0}, videos pending {result.VideosPending:N0}, embeddings added {result.Embedded:N0}, failed {result.Problems.Count:N0} in {result.Duration:hh\\:mm\\:ss}.");
    // Warnings, not failures: the files are processed and the next run computes their embeddings again.
    EmbeddingWarnings.Write(result.EmbeddingProblems, Console.Error);

    Console.WriteLine($"Manifests: {result.ManifestFolder}");
    if (result.Warning is not null)
    {
        Console.Error.WriteLine($"Warning: {result.Warning}");
    }

    foreach (var problem in result.Problems)
    {
        Console.WriteLine($"  {problem.Path}: {problem.Reason}");
    }

    return result.Problems.Count == 0 ? 0 : 1;
}

static async Task<int> RunUpload(string[] args, CancellationToken cancellationToken)
{
    if (!UploadOptions.TryParse(args, out var options, out var error))
    {
        return UsageError(error);
    }

    var progress = new ConsoleProgress<long>(bytes => Console.Error.Write($"\r{ScanReport.FormatBytes(bytes)} sent..."));
    var result = await UploadCommand.RunAsync(options, UploadCommand.CreateClient(options), progress, cancellationToken);
    Console.Error.WriteLine();

    Console.WriteLine($"{result.Assets:N0} assets: uploaded {result.Uploaded:N0} files ({ScanReport.FormatBytes(result.Bytes)}), already there {result.Skipped:N0}, problems {result.Problems.Count:N0} in {result.Duration:hh\\:mm\\:ss}.");
    foreach (var problem in result.Problems)
    {
        Console.WriteLine($"  {problem.Path}: {problem.Reason}");
    }

    return result.Problems.Count == 0 ? 0 : 1;
}

static async Task<int> RunModels(string[] args, CancellationToken cancellationToken)
{
    if (!ModelsOptions.TryParse(args, out var options, out var error))
    {
        return UsageError(error);
    }

    var folder = options.Folder ?? ModelFolder.Default();
    // No overall timeout: the downloader abandons a stalled connection instead (ModelDownloader).
    using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    var downloader = new ModelDownloader(http);
    var failed = false;
    foreach (var model in ModelCatalog.All)
    {
        try
        {
            var progress = new ConsoleProgress<long>(bytes => Console.Error.Write($"\r{model.Name}: {ScanReport.FormatBytes(bytes)}..."));
            var outcome = await downloader.EnsureAsync(model, folder, progress, cancellationToken);
            Console.Error.WriteLine();
            Console.WriteLine(outcome == ModelDownloader.Outcome.Downloaded
                ? $"{model.Name}: downloaded and verified ({model.License})."
                : $"{model.Name}: already present and verified.");
        }
        catch (Exception ex) when (ex is ModelIntegrityException or HttpRequestException or IOException or TimeoutException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"{model.Name}: {ex.Message}");
            failed = true;
        }
    }

    Console.WriteLine($"Models folder: {folder}");
    return failed ? 1 : 0;
}

static int UsageError(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(Usage);
    return 2;
}
