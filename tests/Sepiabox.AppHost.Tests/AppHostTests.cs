using System.Net;
using Npgsql;

namespace Sepiabox.AppHost.Tests;

[Collection(AppHostGroup.Name)]
[Trait("Category", "Integration")]
public sealed class AppHostTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Web_health_returns_ok_when_database_and_storage_are_up()
    {
        using var http = fixture.App.CreateHttpClient("web");

        using var response = await http.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Home_page_shows_the_product_name()
    {
        using var http = fixture.App.CreateHttpClient("web");

        using var response = await http.GetAsync(new Uri("/", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sepiabox", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_database_supports_pgvector_and_postgis()
    {
        var connectionString = await fixture.App.GetConnectionStringAsync("catalog");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var create = new NpgsqlCommand(
            "CREATE EXTENSION IF NOT EXISTS vector; CREATE EXTENSION IF NOT EXISTS postgis;", connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using var query = new NpgsqlCommand(
            "SELECT count(*) FROM pg_extension WHERE extname IN ('vector', 'postgis')", connection);
        var installed = (long)(await query.ExecuteScalarAsync())!;

        Assert.Equal(2, installed);
    }
}
