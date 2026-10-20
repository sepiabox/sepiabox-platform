using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class Iso6709Tests
{
    [Theory]
    [InlineData("+38.7223-009.1393+010.000/", 38.7223, -9.1393)]
    [InlineData("+38.7223-009.1393/", 38.7223, -9.1393)]
    [InlineData("+38.7223-009.1393", 38.7223, -9.1393)]
    [InlineData("+38.7223-009.1393+10.000000/", 38.7223, -9.1393)]
    [InlineData("-33.8568+151.2153-005.5/", -33.8568, 151.2153)]
    [InlineData(" +40.7128-074.0060/ ", 40.7128, -74.006)]
    [InlineData("+41.18-008.69/", 41.18, -8.69)]
    [InlineData("+38-009/", 38, -9)]
    [InlineData("+38.12345678-009.12345678/", 38.123457, -9.123457)]
    [InlineData("+90.0000+180.0000/", 90, 180)]
    [InlineData("+00.0000+010.0000/", 0, 10)]
    [InlineData("+38.7223-009.1393+010.000/\0", 38.7223, -9.1393)]
    public void Reads_latitude_and_longitude_in_decimal_degrees(string value, double latitude, double longitude)
    {
        Assert.Equal(new GeoPoint(latitude, longitude), Iso6709.Parse(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("+00.0000+000.0000/")]
    [InlineData("-00.0000-000.0000/")]
    [InlineData("+95.0000+000.0000/")]
    [InlineData("+38.7223+181.0000/")]
    [InlineData("+3843.34-00908.36/")]
    [InlineData("+384320-0090820/")]
    [InlineData("38.7223-009.1393/")]
    [InlineData("+38.7223-9.1393/")]
    [InlineData("+38.7223")]
    [InlineData("+38,7223-009,1393/")]
    [InlineData("+38.7223-009.1393+010.000CRSWGS_84/")]
    [InlineData("+38.7223-009.1393//")]
    [InlineData("+38.7223-009.1393/ trailing")]
    [InlineData("+00.0000001-000.0000001/")]
    [InlineData("+\u0663\u0668.7223-009.1393/")]
    [InlineData("+38.7223-\u0660\u0660\u0669.1393/")]
    public void Returns_no_location_for_anything_else(string? value)
    {
        Assert.Null(Iso6709.Parse(value));
    }

    [Fact]
    public void Writes_zero_without_a_sign()
    {
        var point = Iso6709.Parse("-00.0000001+010.0000/");

        Assert.NotNull(point);
        Assert.False(double.IsNegative(point.Latitude));
    }
}
