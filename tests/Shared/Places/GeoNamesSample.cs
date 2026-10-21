using System.Globalization;
using System.IO.Compression;
using System.Text;
using Sepiabox.Worker;

namespace Sepiabox.Tests.Places;

/// <summary>
/// Small synthetic GeoNames dump files, written in the test's folder: GeoNames' data is never committed
/// (CC BY 4.0, D-066). Positions are close to the real ones, so distances between them are realistic. Compiled into
/// the worker's tests and the MVP's acceptance test, which resolves the fixture library's locations against it.
/// </summary>
internal static class GeoNamesSample
{
    public static readonly (long Id, string Name, double Latitude, double Longitude, string Country, string Admin1, long Population)[] Cities =
    [
        (1001, "Porto", 41.14961, -8.61099, "PT", "13", 249_633),
        (1002, "Matosinhos", 41.18207, -8.68908, "PT", "13", 45_703),
        (1003, "Lisbon", 38.71667, -9.13333, "PT", "14", 517_802),
        (1004, "Ponta Delgada", 37.73952, -25.66874, "PT", "20", 20_000),
        (1005, "Madrid", 40.4165, -3.70256, "ES", "29", 3_255_944),
    ];

    public const string Admin1 = "PT.13\tPorto\tPorto\t2735941\nPT.14\tLisbon\tLisbon\t2267056\nPT.20\tAzores\tAzores\t3411865\nES.29\tMadrid\tMadrid\t3117732\n";

    public const string Countries = """
        # GeoNames country info (synthetic sample)
        #ISO	ISO3	ISO-Numeric	fips	Country	Capital
        PT	PRT	620	PO	Portugal	Lisbon
        ES	ESP	724	SP	Spain	Madrid

        """;

    /// <summary>cities500.txt's 19 tab-separated columns for each city.</summary>
    public static string CitiesText(int count = 5)
    {
        var text = new StringBuilder();
        foreach (var city in Cities.Take(count))
        {
            string[] columns =
            [
                city.Id.ToString(CultureInfo.InvariantCulture), city.Name, city.Name, "",
                city.Latitude.ToString(CultureInfo.InvariantCulture), city.Longitude.ToString(CultureInfo.InvariantCulture),
                "P", "PPL", city.Country, "", city.Admin1, "", "", "",
                city.Population.ToString(CultureInfo.InvariantCulture), "", "10", "Europe/Lisbon", "2024-01-01",
            ];
            text.Append(string.Join('\t', columns)).Append('\n');
        }

        return text.ToString();
    }

    public static GeoNamesSource Write(TempFolder folder, int count = 5) => new(
        folder.Write("cities500.txt", Encoding.UTF8.GetBytes(CitiesText(count))),
        folder.Write("admin1CodesASCII.txt", Encoding.UTF8.GetBytes(Admin1)),
        folder.Write("countryInfo.txt", Encoding.UTF8.GetBytes(Countries)));

    /// <summary>cities500.zip as GeoNames serves it: one <c>cities500.txt</c> inside.</summary>
    public static byte[] Zip(int count = 5)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("cities500.txt").Open(), Encoding.UTF8))
        {
            writer.Write(CitiesText(count));
        }

        return buffer.ToArray();
    }

    public static string WriteZip(TempFolder folder)
    {
        var path = Path.Combine(folder.Path, "cities500.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("cities500.txt").Open(), Encoding.UTF8);
        writer.Write(CitiesText());
        return path;
    }
}
