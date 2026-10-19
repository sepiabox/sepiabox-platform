using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Scanning;

public static class ScanReport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void WriteSummary(ScanResult result, TextWriter output)
    {
        output.WriteLine($"Scanned {result.Root} in {FormatDuration(result.Duration)}");
        output.WriteLine();
        output.WriteLine($"{"",-8}{"Files",12}{"Size",14}");
        WriteRow(output, "Photos", result.Totals[MediaKind.Photo]);
        WriteRow(output, "Videos", result.Totals[MediaKind.Video]);
        WriteRow(output, "Other", result.Totals[MediaKind.Other]);
        var all = result.Totals.Values.Aggregate(new KindTotals(0, 0), (sum, t) => new KindTotals(sum.Files + t.Files, sum.Bytes + t.Bytes));
        WriteRow(output, "Total", all);

        if (result.MediaByYear.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Photos and videos by year taken");
            foreach (var (year, count) in result.MediaByYear)
            {
                output.WriteLine($"  {year,-8}{count.ToString("N0", Invariant),10}");
            }
        }

        output.WriteLine();
        if (result.Duplicates is null)
        {
            output.WriteLine("Duplicates: not checked");
        }
        else
        {
            output.WriteLine(
                $"Duplicates: {Count(result.RedundantCopies, "extra copy", "extra copies")} in " +
                $"{Count(result.Duplicates.Count, "group", "groups")}, {FormatBytes(result.RedundantBytes)} that would not need uploading");
        }

        output.WriteLine($"Problems: {Count(result.Problems.Count, "file", "files")} could not be read");
        if (result.Problems.Count > 0)
        {
            output.WriteLine("  Many unreadable files can mean a failing drive. Check its health before going further.");
            foreach (var problem in result.Problems.Take(10))
            {
                output.WriteLine($"  {problem.Path}: {problem.Reason}");
            }

            if (result.Problems.Count > 10)
            {
                output.WriteLine($"  ...and {(result.Problems.Count - 10).ToString("N0", Invariant)} more in the report file.");
            }
        }
    }

    public static void WriteJson(ScanResult result, Stream output) =>
        JsonSerializer.Serialize(output, result, JsonOptions);

    internal static string FormatBytes(long bytes)
    {
        // Decimal units, as cloud storage is priced.
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString("N0", Invariant)} B"
            : $"{value.ToString("N1", Invariant)} {units[unit]}";
    }

    private static string Count(int count, string singular, string plural) =>
        $"{count.ToString("N0", Invariant)} {(count == 1 ? singular : plural)}";

    private static void WriteRow(TextWriter output, string label, KindTotals totals) =>
        output.WriteLine($"{label,-8}{totals.Files.ToString("N0", Invariant),12}{FormatBytes(totals.Bytes),14}");

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
        : duration.TotalMinutes >= 1
            ? $"{duration.Minutes}m {duration.Seconds}s"
            : $"{duration.TotalSeconds.ToString("N1", Invariant)}s";
}
