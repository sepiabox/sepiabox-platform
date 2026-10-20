using System.IO.Compression;
using System.Text;
using Sepiabox.Worker.Places;

namespace Sepiabox.Worker.Tests.Places;

public sealed class GeoNamesFilesTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Reads_the_columns_the_catalog_keeps_from_cities500()
    {
        var source = GeoNamesSample.Write(_folder);

        var cities = GeoNamesFiles.ReadCities(source.Cities).ToList();

        Assert.Equal(5, cities.Count);
        Assert.Equal(new GeoNamesCity(1002, "Matosinhos", "Matosinhos", 41.18207, -8.68908, "PT", "13", 45_703), cities[1]);
    }

    [Fact]
    public void Reads_the_single_text_file_inside_the_zip()
    {
        var cities = GeoNamesFiles.ReadCities(GeoNamesSample.WriteZip(_folder)).ToList();

        Assert.Equal(["Porto", "Matosinhos", "Lisbon", "Ponta Delgada", "Madrid"], cities.Select(c => c.Name));
    }

    [Fact]
    public void Reads_admin1_names_by_country_and_code_and_country_names_skipping_comments()
    {
        var source = GeoNamesSample.Write(_folder);

        var admin1 = GeoNamesFiles.ReadAdmin1(source.Admin1);
        var countries = GeoNamesFiles.ReadCountries(source.Countries);

        Assert.Equal("Porto", admin1["PT.13"]);
        Assert.Equal(4, admin1.Count);
        Assert.Equal(new Dictionary<string, string> { ["PT"] = "Portugal", ["ES"] = "Spain" }, countries);
    }

    [Theory]
    [InlineData("1001\tPorto\tPorto")]
    [InlineData("x\tPorto\tPorto\t\t41.1\t-8.6\tP\tPPL\tPT\t\t13\t\t\t\t0\t\t10\tEurope/Lisbon\t2024-01-01")]
    [InlineData("1001\tPorto\tPorto\t\t95\t-8.6\tP\tPPL\tPT\t\t13\t\t\t\t0\t\t10\tEurope/Lisbon\t2024-01-01")]
    [InlineData("1001\t\tPorto\t\t41.1\t-8.6\tP\tPPL\tPT\t\t13\t\t\t\t0\t\t10\tEurope/Lisbon\t2024-01-01")]
    [InlineData("1001\tPorto\tPorto\t\tNaN\t-8.6\tP\tPPL\tPT\t\t13\t\t\t\t0\t\t10\tEurope/Lisbon\t2024-01-01")]
    [InlineData("1001\tPorto\tPorto\t\t41.1\tNaN\tP\tPPL\tPT\t\t13\t\t\t\t0\t\t10\tEurope/Lisbon\t2024-01-01")]
    public void A_malformed_line_fails_with_the_file_and_line_number(string line)
    {
        var path = _folder.Write("cities500.txt", Encoding.UTF8.GetBytes(GeoNamesSample.CitiesText(2) + line + "\n"));

        var error = Assert.Throws<InvalidDataException>(() => GeoNamesFiles.ReadCities(path).ToList());

        Assert.StartsWith("cities500.txt, line 3:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_admin1_or_country_code_fails_with_its_line()
    {
        var admin1 = _folder.Write("admin1CodesASCII.txt", Encoding.UTF8.GetBytes(GeoNamesSample.Admin1 + "PT.13\tPorto again\tPorto\t1\n"));
        var countries = _folder.Write("countryInfo.txt", Encoding.UTF8.GetBytes(GeoNamesSample.Countries + "PT\tPRT\t620\tPO\tPortugal\n"));

        Assert.Equal("admin1CodesASCII.txt, line 5: duplicate code.", Assert.Throws<InvalidDataException>(() => GeoNamesFiles.ReadAdmin1(admin1)).Message);
        Assert.StartsWith("countryInfo.txt, line ", Assert.Throws<InvalidDataException>(() => GeoNamesFiles.ReadCountries(countries)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_zip_without_exactly_one_text_file_fails_clearly(int textFiles)
    {
        var path = Path.Combine(_folder.Path, "cities500.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.md");
            for (var i = 0; i < textFiles; i++)
            {
                using var writer = new StreamWriter(zip.CreateEntry($"cities{i}.txt").Open());
                writer.Write(GeoNamesSample.CitiesText(1));
            }
        }

        var error = Assert.Throws<InvalidDataException>(() => GeoNamesFiles.ReadCities(path).ToList());

        Assert.Equal("cities500.zip: expected exactly one .txt file, as in GeoNames' cities500.zip.", error.Message);
    }

    [Fact]
    public void A_file_with_more_cities_than_the_ceiling_fails_clearly()
    {
        var source = GeoNamesSample.Write(_folder);

        var text = Assert.Throws<InvalidDataException>(() => GeoNamesFiles.ReadCities(source.Cities, maxCities: 3).ToList());
        var zipped = Assert.Throws<InvalidDataException>(() => GeoNamesFiles.ReadCities(GeoNamesSample.WriteZip(_folder), maxCities: 3).ToList());

        Assert.Equal("cities500.txt has more than 3 lines; cities500 has about 200,000, so this is not that file.", text.Message);
        Assert.Equal("cities500.zip has more than 3 lines; cities500 has about 200,000, so this is not that file.", zipped.Message);
        Assert.Equal(1_000_000, GeoNamesFiles.MaxCities);
    }
}
