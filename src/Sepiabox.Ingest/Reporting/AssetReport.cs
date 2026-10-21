using System.Globalization;
using System.Text.Json;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Reporting;

/// <summary>Unique photos and videos captured in one year.</summary>
public sealed record YearCount(int Year, int Photos, int Videos)
{
    public int Total => Photos + Videos;
}

/// <summary>A manifest that was not counted, and why.</summary>
public sealed record ReportProblem(string Path, string Reason);

/// <summary>
/// What <c>report</c> counts (S-062, D-074): unique assets, the catalog's unit (D-014), per year of their capture date
/// as stored (D-037) and kind, the undated ones, and how many were found at more than one path.
/// </summary>
public sealed record AssetReport(
    IReadOnlyList<string> WorkFolders,
    IReadOnlyList<YearCount> Years,
    int UndatedPhotos,
    int UndatedVideos,
    int MultipleSources,
    IReadOnlyList<ReportProblem> Problems)
{
    public int Undated => UndatedPhotos + UndatedVideos;

    public int Total => Years.Sum(y => y.Total) + Undated;

    /// <summary>
    /// Reads every manifest of the work folders, given in upload order, and merges the ones of the same asset as
    /// the cloud does (<see cref="ManifestMerge"/>, D-036, D-046): an asset counts once, with the merged date and kind.
    /// Only reads: nothing in the work folders or the user's files is changed.
    /// </summary>
    public static AssetReport Build(IReadOnlyList<string> workFolders, IManifestValidator validator, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workFolders);
        ArgumentNullException.ThrowIfNull(validator);
        var assets = new Dictionary<string, Manifest>(StringComparer.Ordinal);
        var problems = new List<ReportProblem>();
        foreach (var folder in workFolders)
        {
            var manifests = Path.Combine(folder, "manifests");
            if (!Directory.Exists(manifests))
            {
                throw new DirectoryNotFoundException($"{Path.GetFullPath(folder)} is not a process work folder: it has no manifests folder.");
            }

            foreach (var path in Directory.EnumerateFiles(manifests, "*.json").Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!validator.TryValidate(File.ReadAllText(path), out var manifest, out var problem))
                {
                    problems.Add(new ReportProblem(Path.GetFullPath(path), problem));
                    continue;
                }

                // Embeddings and faces are not counted and are most of a manifest's size: dropped, so 50,000
                // assets fit in memory. The first manifest of an asset is stored canonical, as the cloud writes it.
                var light = Light(manifest);
                assets[manifest.Id] = assets.TryGetValue(manifest.Id, out var earlier)
                    ? Light(ManifestMerge.Merge(earlier, light))
                    : ManifestMerge.Canonical(light);
            }
        }

        var counted = assets.Values.Where(a => a.Kind is MediaKind.Photo or MediaKind.Video).ToList();
        var years = counted
            .Where(a => a.CapturedAt is not null)
            .GroupBy(a => a.CapturedAt!.Value.Year)
            .OrderBy(g => g.Key)
            .Select(g => new YearCount(g.Key, g.Count(a => a.Kind == MediaKind.Photo), g.Count(a => a.Kind == MediaKind.Video)))
            .ToList();
        return new AssetReport(
            [.. workFolders.Select(Path.GetFullPath)],
            years,
            counted.Count(a => a.CapturedAt is null && a.Kind == MediaKind.Photo),
            counted.Count(a => a.CapturedAt is null && a.Kind == MediaKind.Video),
            counted.Count(a => a.Sources.Count > 1),
            problems);
    }

    // What the counts need of a manifest; whether faces were looked for stays, as the merge compares it.
    private static Manifest Light(Manifest manifest) =>
        manifest with { Embeddings = new Dictionary<string, System.Text.Json.Nodes.JsonNode>(), Faces = [] };

    /// <summary>The table people read.</summary>
    public void WriteTable(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var folders = WorkFolders.Count == 1 ? "1 work folder" : $"{WorkFolders.Count} work folders";
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Unique photos and videos in {folders}, as the catalog counts them:"));
        output.WriteLine();
        output.WriteLine($"{"Year",-8}{"Photos",10}{"Videos",10}{"Total",10}");
        foreach (var year in Years)
        {
            output.WriteLine(Row(year.Year.ToString(CultureInfo.InvariantCulture), year.Photos, year.Videos, year.Total));
        }

        output.WriteLine(Row("Undated", UndatedPhotos, UndatedVideos, Undated));
        output.WriteLine(Row("Total", Years.Sum(y => y.Photos) + UndatedPhotos, Years.Sum(y => y.Videos) + UndatedVideos, Total));
        output.WriteLine();
        output.WriteLine(MultipleSources == 1
            ? "1 asset was found at more than one path."
            : string.Create(CultureInfo.InvariantCulture, $"{MultipleSources:N0} assets were found at more than one path."));
        if (Problems.Count > 0)
        {
            output.WriteLine();
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Problems.Count:N0} manifests could not be read and are not counted:"));
            foreach (var problem in Problems)
            {
                output.WriteLine($"  {problem.Path}: {problem.Reason}");
            }
        }
    }

    /// <summary>The same numbers as JSON, in the shape of <c>docs/schemas/ingest-report.v1.json</c> (what S-054 reads).</summary>
    public void WriteJson(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var document = new JsonReport(
            1,
            WorkFolders,
            Total,
            [.. Years.Select(y => new JsonYear(y.Year, y.Photos, y.Videos, y.Total))],
            new JsonCounts(UndatedPhotos, UndatedVideos, Undated),
            MultipleSources,
            [.. Problems.Select(p => new JsonProblem(p.Path, p.Reason))]);
        JsonSerializer.Serialize(output, document, s_json);
    }

    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static string Row(string label, int photos, int videos, int total) =>
        string.Create(CultureInfo.InvariantCulture, $"{label,-8}{photos,10:N0}{videos,10:N0}{total,10:N0}");

    private sealed record JsonReport(
        int SchemaVersion,
        IReadOnlyList<string> WorkFolders,
        int Total,
        IReadOnlyList<JsonYear> Years,
        JsonCounts Undated,
        int MultipleSources,
        IReadOnlyList<JsonProblem> Problems);

    private sealed record JsonYear(int Year, int Photos, int Videos, int Total);

    private sealed record JsonCounts(int Photos, int Videos, int Total);

    private sealed record JsonProblem(string Path, string Reason);
}
