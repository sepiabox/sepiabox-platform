using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class VideoDurationsTests
{
    [Theory]
    [InlineData(0.3, "0:00")]
    [InlineData(3, "0:03")]
    [InlineData(3.9, "0:03")]
    [InlineData(59.5, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(75, "1:15")]
    [InlineData(600, "10:00")]
    [InlineData(3599.9, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    [InlineData(36_000, "10:00:00")]
    [InlineData(1e9, "277777:46:40")]
    public void Writes_m_ss_or_h_mm_ss_rounded_down_when_the_length_is_known(double seconds, string expected) =>
        Assert.Equal(expected, VideoDurations.Format(seconds));

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e9 + 1)]
    [InlineData(1e300)]
    public void Writes_nothing_when_the_length_is_unknown_or_impossible(double? seconds) =>
        Assert.Null(VideoDurations.Format(seconds));
}
