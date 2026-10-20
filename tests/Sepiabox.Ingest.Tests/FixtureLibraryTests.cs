using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// The fixture library of S-061 against the real <c>process</c> and FFmpeg: what S-054 relies on. The manifests
/// are counted here directly; S-054 compares the same numbers with <c>sepiabox-ingest report</c> (S-062).
/// </summary>
[Trait("Category", "Integration")]
public sealed class FixtureLibraryTests
{
    private const int Seed = 61;

    private static readonly FfmpegTools s_tools = FfmpegTools.Find(null)
        ?? throw new InvalidOperationException("Integration tests need ffmpeg and ffprobe on the PATH.");

    [Fact]
    public void Writes_the_same_files_and_numbers_when_the_seed_is_the_same()
    {
        using var first = new TempFolder();
        using var second = new TempFolder();
        using var other = new TempFolder();

        var expected = FixtureLibrary.Write(first.Path, Seed, s_tools.Ffmpeg);
        var again = FixtureLibrary.Write(second.Path, Seed, s_tools.Ffmpeg);
        var different = FixtureLibrary.Write(other.Path, Seed + 1, s_tools.Ffmpeg);

        Assert.Equal(Hashes(first), Hashes(second));
        Assert.Equal(expected.Files, again.Files);
        Assert.Equal(expected.ByYearAndKind, again.ByYearAndKind);
        Assert.Equal(expected.ByCity, again.ByCity);
        Assert.Equal((expected.Undated, expected.FolderDated, expected.Duplicates), (again.Undated, again.FolderDated, again.Duplicates));
        Assert.Equal(expected.Files.Select(f => f.RelativePath), Hashes(first).Keys);
        // Another seed draws other dates (and possibly other counts) for the same cases.
        Assert.NotEqual(
            expected.Files.Where(f => f.Date == FixtureDate.Metadata).Select(f => f.Year),
            different.Files.Where(f => f.Date == FixtureDate.Metadata).Select(f => f.Year));
        Assert.Equal(expected.Count(1901, MediaKind.Photo), different.Count(1901, MediaKind.Photo));
    }

    [Fact]
    public void Holds_every_case_the_mvp_acceptance_needs_when_generated()
    {
        using var drive = new TempFolder();

        var expected = FixtureLibrary.Write(drive.Path, Seed, s_tools.Ffmpeg);

        Assert.Equal(1, expected.Duplicates);
        Assert.Equal(4, expected.FolderDated);
        Assert.Equal(2, expected.Count(1901, MediaKind.Photo));
        Assert.Equal((2, 1), (expected.UndatedOf(MediaKind.Photo), expected.UndatedOf(MediaKind.Video)));
        Assert.Contains(expected.Assets, a => a is { Kind: MediaKind.Video, City: FixtureLibrary.Lisbon });
        Assert.Contains(expected.Assets, a => a is { Kind: MediaKind.Video, City: FixtureLibrary.Porto });
        Assert.Contains(expected.Assets, a => a is { Kind: MediaKind.Photo, Date: FixtureDate.Metadata, Location: null });
        Assert.Contains(expected.Files, f => f.Kind == MediaKind.Other);
        Assert.Equal(expected.MediaFiles - expected.Duplicates, expected.Assets.Count);
        Assert.Equal(expected.Assets.Count, expected.ByYearAndKind.Sum(e => e.Value) + expected.Undated);

        // On disk too: the copy is byte for byte the original, and each folder-dated file's parent is a day.
        var duplicate = Assert.Single(expected.Files, f => f.DuplicateOf is not null);
        Assert.Equal(Sha256(drive, duplicate.DuplicateOf!), Sha256(drive, duplicate.RelativePath));
        // Accented names, composed (NFC), exactly as they are on disk.
        Assert.Contains(expected.Files, f => f.RelativePath.Contains('ã', StringComparison.Ordinal));
        Assert.All(expected.Files, f => Assert.True(f.RelativePath.IsNormalized(NormalizationForm.FormC), f.RelativePath));
        Assert.Equal(
            expected.Files.Select(f => f.RelativePath).Order(StringComparer.Ordinal),
            Directory.GetFiles(drive.Path, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(drive.Path, f).Replace('\\', '/')).Order(StringComparer.Ordinal));
        foreach (var file in expected.Files.Where(f => f.Date == FixtureDate.Folder))
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(Path.Combine(drive.Path, file.RelativePath)))!;
            Assert.True(
                DateTime.TryParseExact(folder, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day.Year == file.Year,
                $"{file.RelativePath}: its folder {folder} is not a day in {file.Year}.");
        }
    }

    [Theory]
    [InlineData(Seed)]
    [InlineData(7)]
    [InlineData(2026)]
    public void Process_turns_the_library_into_the_expected_unique_assets(int seed)
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        var expected = FixtureLibrary.Write(drive.Path, seed, s_tools.Ffmpeg);

        var result = Process(drive, work);

        Assert.Empty(result.Problems);
        Assert.Empty(result.LeftOut);
        Assert.Equal(0, result.VideosPending);
        var manifests = ReadManifests(work);
        // One manifest per unique asset: the exact duplicate is one manifest with both paths as sources.
        Assert.Equal(expected.Assets.Count, manifests.Count);
        var duplicate = Assert.Single(expected.Files, f => f.DuplicateOf is not null);
        Assert.Equal(
            new[] { duplicate.DuplicateOf!, duplicate.RelativePath }.Order(StringComparer.Ordinal),
            Assert.Single(manifests, m => m.Sources.Count > 1).Sources.Select(s => s.Path).Order(StringComparer.Ordinal));

        var byYearAndKind = manifests.Where(m => m.CapturedAt is not null)
            .GroupBy(m => (Year: m.CapturedAt!.Value.Year, m.Kind))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Kind)
            .Select(g => KeyValuePair.Create(g.Key, g.Count()));
        Assert.Equal(expected.ByYearAndKind, byYearAndKind);
        Assert.Equal(expected.Undated, manifests.Count(m => m.CapturedAt is null));
        Assert.Equal(expected.FolderDated, manifests.Count(m => m.CapturedAtSource == CaptureDateSource.Folder));
        Assert.Equal(2, manifests.Count(m => m.CapturedAt?.Year == 1901));

        // Each asset as the generator described it, so a mismatch names the file.
        foreach (var asset in expected.Assets)
        {
            var manifest = Assert.Single(manifests, m => m.Sources.Any(s => s.Path == asset.RelativePath));
            Assert.True(
                (manifest.Kind, manifest.CapturedAt?.Year, manifest.Location is not null) == (asset.Kind, asset.Year, asset.Location is not null),
                $"{asset.RelativePath}: expected {asset.Kind} in {asset.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no year"}"
                + $"{(asset.Location is null ? "" : " with a location")}, got {manifest.Kind} in {manifest.CapturedAt?.Year} at {manifest.Location}.");
        }

        // Per city: each manifest's position is one the generator wrote for that city.
        var byCity = manifests.Where(m => m.Location is not null)
            .GroupBy(m => FixtureLibrary.CityPoints.Single(p => Near(p.Point, m.Location!)).City)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => KeyValuePair.Create(g.Key, g.Count()));
        Assert.Equal(expected.ByCity, byCity);
    }

    [Fact]
    public void Process_records_the_locations_the_generator_wrote_into_the_videos()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        var expected = FixtureLibrary.Write(drive.Path, Seed, s_tools.Ffmpeg);

        Process(drive, work);

        var manifests = ReadManifests(work);
        var videos = expected.Assets.Where(a => a.Kind == MediaKind.Video).ToList();
        Assert.Equal(2, videos.Count(v => v.Location is not null));
        foreach (var video in videos)
        {
            var manifest = Assert.Single(manifests, m => m.Sources.Any(s => s.Path == video.RelativePath));
            if (video.Location is null)
            {
                Assert.Null(manifest.Location);
            }
            else
            {
                Assert.True(manifest.Location is not null && Near(video.Location, manifest.Location), $"{video.RelativePath}: expected {video.Location}, got {manifest.Location}.");
            }
        }
    }

    // ISO 6709 in the videos and EXIF seconds to the hundredth both keep the position well within a metre or so.
    private static bool Near(GeoPoint expected, GeoPoint actual) =>
        Math.Abs(expected.Latitude - actual.Latitude) < 0.0001 && Math.Abs(expected.Longitude - actual.Longitude) < 0.0001;

    private static string Sha256(TempFolder folder, string relativePath) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder.Path, relativePath))));

    private static SortedDictionary<string, string> Hashes(TempFolder folder) =>
        new(Directory.GetFiles(folder.Path, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(folder.Path, file).Replace('\\', '/'),
            file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))), StringComparer.Ordinal);

    private static ProcessResult Process(TempFolder drive, TempFolder work) =>
        ProcessCommand.Run(new ProcessOptions(
            drive.Path, work.Path, "HD", FfmpegFolder: Path.GetDirectoryName(s_tools.Ffmpeg), ModelFolder: TestModels.None));

    private static List<Manifest> ReadManifests(TempFolder work) =>
        [.. Directory.GetFiles(Path.Combine(work.Path, "manifests"), "*.json").Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];
}
