using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Tests;

public sealed class PgvectorSimilarityTests
{
    [Theory]
    [InlineData("0.8.0", true)]
    [InlineData("0.8.1", true)]
    [InlineData("0.8", true)]
    [InlineData("1.0.0", true)]
    [InlineData("0.7.4", false)]
    [InlineData("0.5.1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("dev", false)]
    public void Needs_pgvector_0_8_for_iterative_scans(string? version, bool supported)
    {
        Assert.Equal(supported, PgvectorSimilarity.SupportsIterativeScan(version));
    }
}
