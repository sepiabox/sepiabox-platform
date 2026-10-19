using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Sepiabox.Web.Tests;

[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class PhotoDetailTests(CatalogFixture catalog)
{
    [Theory]
    [InlineData(10)]
    [InlineData(null)]
    public async Task A_photo_that_does_not_exist_is_a_404(int? existing)
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        await using var web = CreateWeb(catalog.ConnectionStringFor(db));
        using var http = web.CreateClient();

        // A well-formed id that isn't in the catalog, and a malformed one.
        var id = existing is { } n ? Ids.Of(n) : "not-an-id";
        using var response = await http.GetAsync(new Uri($"/fotos/{id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_existing_photo_shows_its_details_and_a_link_to_the_original()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(11), new DateTime(2019, 7, 14, 10, 32, 0)));
        await db.SaveChangesAsync();
        await using var web = CreateWeb(catalog.ConnectionStringFor(db));
        using var http = web.CreateClient();

        using var response = await http.GetAsync(new Uri($"/fotos/{Ids.Of(11)}", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("14 de julho de 2019, 10:32", html, StringComparison.Ordinal);
        Assert.Contains("Apple iPhone 15", html, StringComparison.Ordinal);
        Assert.Contains($"/originals/{Ids.Of(11)}?", html, StringComparison.Ordinal);
        Assert.Contains("HD Ana", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_timeline_renders_against_a_real_catalog()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(12), new DateTime(2026, 10, 1)));
        await db.SaveChangesAsync();
        await using var web = CreateWeb(catalog.ConnectionStringFor(db));
        using var http = web.CreateClient();

        var html = await http.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("outubro de 2026", html, StringComparison.Ordinal);
        Assert.Contains($"fotos/{Ids.Of(12)}", html, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateWeb(string catalogConnectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseSetting("ConnectionStrings:catalog", catalogConnectionString)
            // Signing works offline with the emulator's well-known key; nothing is fetched from storage.
            .UseSetting("ConnectionStrings:blobs", "UseDevelopmentStorage=true"));
}
