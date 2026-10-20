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
    public void A_malformed_line_fails_with_the_file_and_line_number(string line)
    {
        var path = _folder.Write("cities500.txt", Encoding.UTF8.GetBytes(GeoNamesSample.CitiesText(2) + line + "\n"));

        var error = Assert.Throws<InvalidDataException>(() => GeoNamesFiles.ReadCities(path).ToList());

        Assert.StartsWith("cities500.txt, line 3:", error.Message, StringComparison.Ordinal);
    }
}
