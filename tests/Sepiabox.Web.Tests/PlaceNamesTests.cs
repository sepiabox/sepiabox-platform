using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class PlaceNamesTests
{
    [Theory]
    [InlineData("Matosinhos", "Porto", "Portugal", "Matosinhos, Porto, Portugal")]
    [InlineData("Lisbon", "Lisbon", "Portugal", "Lisbon, Portugal")]
    [InlineData("Monaco", null, "Monaco", "Monaco, Monaco")]
    [InlineData("Matosinhos", "", null, "Matosinhos")]
    [InlineData(null, "Porto", "Portugal", null)]
    public void Joins_city_region_and_country_when_named(string? city, string? region, string? country, string? expected) =>
        Assert.Equal(expected, PlaceNames.Format(city, region, country));
}
