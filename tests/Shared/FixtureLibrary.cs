using System.Diagnostics;
using System.Globalization;
using ImageMagick;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Tests.Shared;

/// <summary>How a fixture file gets its capture date (D-046).</summary>
internal enum FixtureDate
{
    /// <summary>EXIF <c>DateTimeOriginal</c> or the QuickTime creation time.</summary>
    Metadata,

    /// <summary>Only its parent folder, named <c>YYYY-MM-DD</c>.</summary>
    Folder,

    /// <summary>No metadata date, no dated file name and no dated parent folder.</summary>
    None,
}

/// <summary>
/// One file the library wrote. <paramref name="Year"/> is the year the pipeline must give it (null when undated),
/// <paramref name="City"/> the GeoNames city its location resolves to with the synthetic sample of S-012, and
/// <paramref name="DuplicateOf"/> the file whose exact copy it is (it is then the same asset).
/// </summary>
internal sealed record FixtureFile(
    string RelativePath,
    MediaKind Kind,
    FixtureDate Date,
    int? Year,
    GeoPoint? Location,
    string? City,
    string? DuplicateOf = null);

/// <summary>
/// What a generated library must become, in unique assets (an exact duplicate counts once): the numbers S-054
/// asks the catalog for. Built from <see cref="Files"/>, so the files and the numbers cannot disagree.
/// </summary>
internal sealed record FixtureExpectations(IReadOnlyList<FixtureFile> Files)
{
    /// <summary>One entry per asset: every media file except the copies.</summary>
    public IReadOnlyList<FixtureFile> Assets { get; } =
        [.. Files.Where(f => f.Kind != MediaKind.Other && f.DuplicateOf is null)];

    public int MediaFiles => Files.Count(f => f.Kind != MediaKind.Other);

    public int Duplicates => Files.Count(f => f.DuplicateOf is not null);

    /// <summary>Assets per (year, kind), dated by metadata or by folder; sorted by year, then photo before video.</summary>
    public IReadOnlyList<KeyValuePair<(int Year, MediaKind Kind), int>> ByYearAndKind =>
        [.. Assets.Where(a => a.Year is not null)
            .GroupBy(a => (Year: a.Year!.Value, a.Kind))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Kind)
            .Select(g => KeyValuePair.Create(g.Key, g.Count()))];

    public int Count(int year, MediaKind kind) => Assets.Count(a => a.Year == year && a.Kind == kind);

    public int FolderDated => Assets.Count(a => a.Date == FixtureDate.Folder);

    /// <summary>Assets with no date at all, which time-filtered answers report as "N items have no date".</summary>
    public int Undated => Assets.Count(a => a.Date == FixtureDate.None);

    public int UndatedOf(MediaKind kind) => Assets.Count(a => a.Date == FixtureDate.None && a.Kind == kind);

    /// <summary>Assets per GeoNames city, sorted by name; assets without a location are in no city.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> ByCity =>
        [.. Assets.Where(a => a.City is not null)
            .GroupBy(a => a.City!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => KeyValuePair.Create(g.Key, g.Count()))];

    public int InCity(string city) => Assets.Count(a => a.City == city);
}

/// <summary>
/// Writes a small library of photos and videos whose contents are known in advance (S-061), for the MVP's
/// end-to-end test (S-054): EXIF-dated photos, some located in Lisbon and Porto; phone-style MOV videos with a
/// QuickTime creation time and an ISO 6709 location (S-053); files dated only by a <c>YYYY-MM-DD</c> parent folder,
/// one of them in 1901 (D-046); truly undated files; an exact duplicate at a second path; and a text file that
/// is not media. Nothing is committed: JPEGs come from Magick.NET and videos from FFmpeg, and the same seed
/// writes the same bytes and the same numbers (D-068).
/// </summary>
internal static class FixtureLibrary
{
    public const string Lisbon = "Lisbon";
    public const string Porto = "Porto";

    /// <summary>
    /// Every position a fixture file may carry, by the city it resolves to with the S-012 GeoNames sample: a few
    /// hundred metres around each centre, far nearer to it than to any other sample city (Matosinhos is 7 km
    /// from Porto). A Worker test checks each one against that sample.
    /// </summary>
    public static IReadOnlyList<(string City, GeoPoint Point)> CityPoints { get; } =
    [
        (Lisbon, new GeoPoint(38.7223, -9.1393)),
        (Lisbon, new GeoPoint(38.7139, -9.1335)),
        (Lisbon, new GeoPoint(38.7169, -9.1399)),
        (Porto, new GeoPoint(41.1496, -8.6110)),
        (Porto, new GeoPoint(41.1458, -8.6111)),
        (Porto, new GeoPoint(41.1413, -8.6131)),
    ];

    /// <summary>The folder date old enough that only a person's folder name can give it (D-046 allows 1900 on).</summary>
    public const string FarPastFolder = "1901-05-20";

    /// <summary>
    /// Writes the library into the empty folder <paramref name="root"/> with <paramref name="ffmpeg"/> making the
    /// videos, and returns what the pipeline must make of it.
    /// </summary>
    public static FixtureExpectations Write(string root, int seed, string ffmpeg)
    {
        var random = new Random(seed);
        var files = new List<FixtureFile>();
        var n = 0;
        string Unique() => string.Create(CultureInfo.InvariantCulture, $"sepiabox-fixture-{seed}-{n++}");

        // Photos dated by EXIF: every fourth in Lisbon, the next in Porto, the rest without GPS.
        var dated = 8 + random.Next(5);
        for (var i = 0; i < dated; i++)
        {
            var taken = RandomDay(random, 2003, 2024);
            var (city, point) = (i % 4) switch
            {
                0 => Located(Lisbon, i / 4),
                1 => Located(Porto, i / 4),
                _ => (null, null),
            };
            var path = Path.Combine("Fotos", string.Create(CultureInfo.InvariantCulture, $"IMG_{i:0000}.jpg"));
            Write(root, path, Photo(Unique(), taken, point));
            files.Add(new FixtureFile(Slash(path), MediaKind.Photo, FixtureDate.Metadata, taken.Year, point, city));
        }

        // An exact copy of the first photo, as when a folder was copied to two places: one asset.
        var copied = files[0];
        var copy = Path.Combine("Copia de Fotos", Path.GetFileName(copied.RelativePath));
        Write(root, copy, File.ReadAllBytes(Path.Combine(root, copied.RelativePath)));
        files.Add(copied with { RelativePath = Slash(copy), DuplicateOf = copied.RelativePath });

        // Phone videos with a creation time; the first in Lisbon, the second in Porto, the rest without a location.
        var videos = 3 + random.Next(3);
        for (var i = 0; i < videos; i++)
        {
            var created = RandomDay(random, 2012, 2024);
            var (city, point) = i switch
            {
                0 => Located(Lisbon, 0),
                1 => Located(Porto, 0),
                _ => (null, null),
            };
            var path = Path.Combine("Videos", string.Create(CultureInfo.InvariantCulture, $"VID_{i:000}.mov"));
            Video(ffmpeg, root, path, Unique(), created, point);
            files.Add(new FixtureFile(Slash(path), MediaKind.Video, FixtureDate.Metadata, created.Year, point, city));
        }

        // Dated only by their parent folder: scans of old prints and a camcorder clip.
        var folderDay = RandomDay(random, 1995, 2016).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var (folder, name, kind) in new[]
        {
            (FarPastFolder, "digitalizacao-a.jpg", MediaKind.Photo),
            (FarPastFolder, "digitalizacao-b.jpg", MediaKind.Photo),
            (folderDay, "foto.jpg", MediaKind.Photo),
            (folderDay, "filme.mov", MediaKind.Video),
        })
        {
            var path = Path.Combine("Arquivo", folder, name);
            WriteMedia(ffmpeg, root, path, kind, Unique());
            files.Add(new FixtureFile(Slash(path), kind, FixtureDate.Folder, int.Parse(folder[..4], CultureInfo.InvariantCulture), null, null));
        }

        // Truly undated: no metadata date, no date in the name, and a parent folder that is not a day.
        foreach (var (name, kind) in new[]
        {
            ("sem-data.jpg", MediaKind.Photo),
            ("outra-sem-data.jpg", MediaKind.Photo),
            ("clipe.mov", MediaKind.Video),
        })
        {
            var path = Path.Combine("Diversos", name);
            WriteMedia(ffmpeg, root, path, kind, Unique());
            files.Add(new FixtureFile(Slash(path), kind, FixtureDate.None, null, null, null));
        }

        // Not media: skipped by scan and process, never an asset.
        var text = Path.Combine("Diversos", "leia-me.txt");
        Write(root, text, System.Text.Encoding.UTF8.GetBytes("Fotos da familia.\n"));
        files.Add(new FixtureFile(Slash(text), MediaKind.Other, FixtureDate.None, null, null, null));

        return new FixtureExpectations([.. files.OrderBy(f => f.RelativePath, StringComparer.Ordinal)]);
    }

    private static (string? City, GeoPoint? Point) Located(string city, int index)
    {
        var points = CityPoints.Where(p => p.City == city).ToList();
        return (city, points[index % points.Count].Point);
    }

    private static void WriteMedia(string ffmpeg, string root, string path, MediaKind kind, string unique)
    {
        if (kind == MediaKind.Video)
        {
            Video(ffmpeg, root, path, unique, created: null, location: null);
        }
        else
        {
            Write(root, path, Photo(unique, taken: null, location: null));
        }
    }

    // Mid-day, so no reading of the time in another zone moves it to another year.
    private static DateTime RandomDay(Random random, int fromYear, int toYear) =>
        new(random.Next(fromYear, toYear), random.Next(1, 13), random.Next(1, 29), 12, random.Next(0, 60), random.Next(0, 60));

    /// <summary>A 64×48 JPEG; <c>ImageUniqueID</c> makes each file's bytes, and so its asset, distinct.</summary>
    private static byte[] Photo(string unique, DateTime? taken, GeoPoint? location)
    {
        using var image = new MagickImage(new MagickColor(Colour(unique)), 64, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.ImageUniqueID, unique);
        if (taken is { } date)
        {
            exif.SetValue(ExifTag.Make, "Apple");
            exif.SetValue(ExifTag.Model, "iPhone 8");
            exif.SetValue(ExifTag.DateTimeOriginal, date.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture));
        }

        if (location is { } at)
        {
            exif.SetValue(ExifTag.GPSLatitudeRef, at.Latitude >= 0 ? "N" : "S");
            exif.SetValue(ExifTag.GPSLatitude, Dms(at.Latitude));
            exif.SetValue(ExifTag.GPSLongitudeRef, at.Longitude >= 0 ? "E" : "W");
            exif.SetValue(ExifTag.GPSLongitude, Dms(at.Longitude));
        }

        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    // Degrees, minutes and seconds to the hundredth, as phones write them.
    private static Rational[] Dms(double value)
    {
        var hundredths = (long)Math.Round(Math.Abs(value) * 360_000);
        return [new Rational((uint)(hundredths / 360_000), 1), new Rational((uint)(hundredths / 6000 % 60), 1), new Rational((uint)(hundredths % 6000), 100)];
    }

    private static string Colour(string unique)
    {
        var hash = (uint)unique.Aggregate(17, (h, c) => (h * 31) + c);
        return string.Create(CultureInfo.InvariantCulture, $"#{hash & 0xFFFFFF:x6}");
    }

    /// <summary>
    /// A one-second 160×90 MOV, written bit-exact so the same arguments give the same bytes. The creation time goes
    /// into <c>mvhd</c> and the position into <c>udta/©xyz</c>, as older iPhones and Android phones write them;
    /// without a creation time FFmpeg writes 0 (1904), which the pipeline rejects as implausible (D-046).
    /// </summary>
    private static void Video(string ffmpeg, string root, string path, string unique, DateTime? created, GeoPoint? location)
    {
        var target = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        List<string> arguments =
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", "testsrc=duration=1:size=160x90:rate=10",
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
            "-fflags", "+bitexact", "-flags:v", "+bitexact",
            "-metadata", "title=" + unique,
        ];
        if (created is { } date)
        {
            arguments.AddRange(["-metadata", "creation_time=" + date.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)]);
        }

        if (location is { } at)
        {
            arguments.AddRange(["-metadata", string.Create(CultureInfo.InvariantCulture, $"location={at.Latitude:+00.0000;-00.0000}{at.Longitude:+000.0000;-000.0000}/")]);
        }

        arguments.Add(target);
        var start = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {ffmpeg}.");
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg could not write {path}: {errors}");
        }
    }

    private static void Write(string root, string path, byte[] content)
    {
        var target = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, content);
    }

    private static string Slash(string path) => path.Replace('\\', '/');
}
