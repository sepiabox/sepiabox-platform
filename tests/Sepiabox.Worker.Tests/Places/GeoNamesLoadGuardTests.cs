using Sepiabox.Worker.Places;

namespace Sepiabox.Worker.Tests.Places;

public sealed class GeoNamesLoadGuardTests
{
    [Theory]
    [InlineData(150_000, 0)]
    [InlineData(200_000, 0)]
    [InlineData(200_000, 210_000)]
    [InlineData(180_000, 200_000)]
    [InlineData(250_000, 200_000)]
    public void Accepts_a_whole_dump(int cities, int previous) =>
        GeoNamesLoadGuard.Default.Check(cities, previous);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(149_999, 0)]
    [InlineData(5, 200_000)]
    public void Refuses_fewer_cities_than_the_floor(int cities, int previous)
    {
        var error = Assert.Throws<InvalidDataException>(() => GeoNamesLoadGuard.Default.Check(cities, previous));

        Assert.Contains("fewer than the 150000 expected at least", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(179_999, 200_000)]
    [InlineData(150_000, 200_000)]
    public void Refuses_more_than_ten_percent_fewer_cities_than_loaded_now(int cities, int previous)
    {
        var error = Assert.Throws<InvalidDataException>(() => GeoNamesLoadGuard.Default.Check(cities, previous));

        Assert.Contains("fewer than the 200000 loaded now", error.Message, StringComparison.Ordinal);
    }
}
