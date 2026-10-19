using System.Net;

using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class TimelinePageTests(CatalogFixture catalog)
{
    private static readonly DateTime PhotoDate = new(2026, 10, 1);

    [Fact]
    public async Task The_timeline_renders_against_a_real_catalog_without_storage_links()
    {
        await using var web = await WebWithPhotoAsync();
        using var http = web.CreateClient();

        var html = await http.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains(PortugueseDates.MonthAndYear(PhotoDate.Year, PhotoDate.Month), html, StringComparison.Ordinal);
        Assert.Contains($"fotos/{Ids.Of(12)}/thumb-256", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("?pagina=999999999")]
    [InlineData("?pagina=2")]
    public async Task A_page_beyond_the_end_says_there_are_no_more_photos(string query)
    {
        await using var web = await WebWithPhotoAsync();
        using var http = web.CreateClient();

        using var response = await http.GetAsync(new Uri("/" + query, UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Não há mais fotos", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("?pagina=abc", null)]
    [InlineData("?pagina=99999999999", "Não há mais fotos")]
    public async Task Malformed_or_huge_page_numbers_are_not_errors(string query, string? expected)
    {
        // Null means page 1, which shows the photo's month.
        expected ??= PortugueseDates.MonthAndYear(PhotoDate.Year, PhotoDate.Month);
        await using var web = await WebWithPhotoAsync();
        using var http = web.CreateClient();

        using var response = await http.GetAsync(new Uri("/" + query, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_photo_page_renders_against_a_real_catalog()
    {
        await using var web = await WebWithPhotoAsync();
        using var http = web.CreateClient();

        var html = await http.GetStringAsync(new Uri($"/fotos/{Ids.Of(12)}", UriKind.Relative));

        Assert.Contains($"fotos/{Ids.Of(12)}/original", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=", html, StringComparison.Ordinal);
    }

    private async Task<WebFactory> WebWithPhotoAsync()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(12), PhotoDate));
        await db.SaveChangesAsync();
        return new WebFactory(catalog.ConnectionStringFor(db));
    }
}
