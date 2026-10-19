using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Ingest.Uploading;

const string Usage = """
    Usage: sepiabox-ingest <command> [options]

    Commands:
      scan <folder>                  Read a folder or drive and report what it holds. Changes nothing.
      process <folder> --out <dir>   Describe every photo and video in one manifest per file content,
                                     with WebP thumbnails and, for videos, a poster and a 720p version.
      upload <dir> --destination local|azure
                                     Send a process work folder to Blob Storage: originals, then
                                     thumbnails and videos, then manifests. Running again resumes.

    Options for scan:
      --report <file>  Also write the full result, with every duplicate and problem, as JSON.
      --no-duplicates  Skip duplicate detection. Faster, but no duplicate count.

    Options for process:
      --out <dir>           Work folder for manifests and progress. Running again resumes.
      --source-name <name>  Name of this drive or folder, such as "HD da Ana". Default: the folder name.
      --ffmpeg <folder>     Folder with ffmpeg and ffprobe. Default: look on the PATH.
      --hw nvenc            Encode the 720p versions on an NVIDIA GPU.

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

    var progress = new Progress<int>(count => Console.Error.Write($"\r{count:N0} files read..."));
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

    var progress = new Progress<int>(count => Console.Error.Write($"\r{count:N0} photos and videos processed..."));
    var result = ProcessCommand.Run(options, progress, cancellationToken);
    Console.Error.WriteLine();

    Console.WriteLine($"Processed {result.Processed:N0}, unchanged {result.Skipped:N0}, videos pending {result.VideosPending:N0}, failed {result.Problems.Count:N0} in {result.Duration:hh\\:mm\\:ss}.");
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

    var progress = new Progress<long>(bytes => Console.Error.Write($"\r{ScanReport.FormatBytes(bytes)} sent..."));
    var result = await UploadCommand.RunAsync(options, UploadCommand.CreateClient(options), progress, cancellationToken);
    Console.Error.WriteLine();

    Console.WriteLine($"{result.Assets:N0} assets: uploaded {result.Uploaded:N0} files ({ScanReport.FormatBytes(result.Bytes)}), already there {result.Skipped:N0}, problems {result.Problems.Count:N0} in {result.Duration:hh\\:mm\\:ss}.");
    foreach (var problem in result.Problems)
    {
        Console.WriteLine($"  {problem.Path}: {problem.Reason}");
    }

    return result.Problems.Count == 0 ? 0 : 1;
}

static int UsageError(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(Usage);
    return 2;
}
