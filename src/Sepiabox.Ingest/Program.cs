using Sepiabox.Ingest.Scanning;

const string Usage = """
    Usage: sepiabox-ingest <command> [options]

    Commands:
      scan <folder>    Read a folder or drive and report what it holds. Changes nothing.

    Options for scan:
      --report <file>  Also write the full result, with every duplicate and problem, as JSON.
      --no-duplicates  Skip duplicate detection. Faster, but no duplicate count.
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return 0;
}

if (args[0] != "scan")
{
    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
    Console.Error.WriteLine(Usage);
    return 2;
}

if (!ScanOptions.TryParse(args[1..], out var options, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(Usage);
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    var progress = new Progress<int>(count => Console.Error.Write($"\r{count:N0} files read..."));
    var result = Scanner.Scan(options, progress, cancellation.Token);
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
catch (DirectoryNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Scan cancelled.");
    return 130;
}
