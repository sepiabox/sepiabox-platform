using System.Globalization;
using System.IO.Compression;

namespace Sepiabox.Worker.Places;

/// <summary>One line of GeoNames' <c>cities500.txt</c>: the columns the catalog keeps.</summary>
public sealed record GeoNamesCity(
    long GeonameId, string Name, string AsciiName, double Latitude, double Longitude, string CountryCode, string Admin1Code, long Population);

/// <summary>
/// Reads GeoNames' dump files (https://download.geonames.org/export/dump/, CC BY 4.0): tab-separated UTF-8
/// without a header. A malformed line fails the load with its line number, rather than loading part of a file.
/// </summary>
public static class GeoNamesFiles
{
    // cities500.txt's columns (readme.txt of the dump): 0 id, 1 name, 2 ascii name, 4 latitude, 5 longitude,
    // 8 country code, 10 admin1 code, 14 population; 19 in all.
    private const int CityColumns = 19;

    /// <summary>The cities of <c>cities500.txt</c>, or of the one <c>.txt</c> inside <c>cities500.zip</c>.</summary>
    /// <remarks>
    /// More than <paramref name="maxCities"/> lines fails the read: cities500 has about 200,000, and the whole
    /// file is held in memory by <c>geonames update</c>, so a far larger file is not the one expected.
    /// </remarks>
    public static IEnumerable<GeoNamesCity> ReadCities(string path, int maxCities = MaxCities)
    {
        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ReadCities(File.OpenRead(path), path, maxCities);
        }

        return ReadZippedCities(path, maxCities);
    }

    /// <summary>The most cities a file may have: five times cities500's.</summary>
    public const int MaxCities = 1_000_000;

    /// <summary><c>admin1CodesASCII.txt</c>: <c>PT.13</c> → <c>Porto</c>.</summary>
    public static Dictionary<string, string> ReadAdmin1(string path)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (number, text) in ReadLines(File.OpenRead(path), path))
        {
            var columns = text.Split('\t');
            if (columns.Length < 2)
            {
                throw Malformed(path, number, "expected the code and the name");
            }

            if (!names.TryAdd(columns[0], columns[1]))
            {
                throw Malformed(path, number, "duplicate code");
            }
        }

        return names;
    }

    /// <summary><c>countryInfo.txt</c>: <c>PT</c> → <c>Portugal</c>; lines starting with <c>#</c> are comments.</summary>
    public static Dictionary<string, string> ReadCountries(string path)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (number, text) in ReadLines(File.OpenRead(path), path).Where(line => !line.Text.StartsWith('#')))
        {
            var columns = text.Split('\t');
            if (columns.Length < 5)
            {
                throw Malformed(path, number, "expected at least 5 columns");
            }

            if (!names.TryAdd(columns[0], columns[4]))
            {
                throw Malformed(path, number, "duplicate code");
            }
        }

        return names;
    }

    private static IEnumerable<GeoNamesCity> ReadZippedCities(string path, int maxCities)
    {
        using var zip = ZipFile.OpenRead(path);
        var texts = zip.Entries.Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        if (texts.Count != 1)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)}: expected exactly one .txt file, as in GeoNames' cities500.zip.");
        }

        foreach (var city in ReadCities(texts[0].Open(), path, maxCities))
        {
            yield return city;
        }
    }

    private static IEnumerable<GeoNamesCity> ReadCities(Stream stream, string path, int maxCities)
    {
        foreach (var (number, text) in ReadLines(stream, path))
        {
            if (number > maxCities)
            {
                throw new InvalidDataException(
                    $"{Path.GetFileName(path)} has more than {maxCities} lines; cities500 has about 200,000, so this is not that file.");
            }

            var columns = text.Split('\t');
            if (columns.Length != CityColumns)
            {
                throw Malformed(path, number, $"expected {CityColumns} columns, found {columns.Length}");
            }

            if (!long.TryParse(columns[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || !double.TryParse(columns[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                || !double.TryParse(columns[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
                || !double.IsFinite(latitude) || !double.IsFinite(longitude)
                || latitude is < -90 or > 90 || longitude is < -180 or > 180
                || columns[1].Length == 0 || columns[8].Length != 2)
            {
                throw Malformed(path, number, "an id, a name, a position and a country code are required");
            }

            var population = long.TryParse(columns[14], NumberStyles.None, CultureInfo.InvariantCulture, out var people) ? people : 0;
            yield return new GeoNamesCity(id, columns[1], columns[2], latitude, longitude, columns[8], columns[10], population);
        }
    }

    private static IEnumerable<(int Number, string Text)> ReadLines(Stream stream, string path)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var number = 0;
        while (reader.ReadLine() is { } line)
        {
            number++;
            if (line.Length > 0)
            {
                yield return (number, line);
            }
        }
    }

    private static InvalidDataException Malformed(string path, int line, string reason) =>
        new($"{Path.GetFileName(path)}, line {line}: {reason}.");
}
