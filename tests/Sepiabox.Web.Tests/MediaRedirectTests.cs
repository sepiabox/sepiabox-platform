using System.Globalization;
using System.Net;
using System.Web;

namespace Sepiabox.Web.Tests;

[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class MediaRedirectTests(CatalogFixture catalog)
{
    [Fact]
    public async Task A_thumbnail_redirects_to_a_cacheable_read_only_link_that_expires_within_15_minutes()
    {
        await using var web = await WebWithPhotoAsync(Ids.Of(1));
        using var http = web.CreateNonRedirectingClient();

        var before = DateTimeOffset.UtcNow;
        using var response = await http.GetAsync(new Uri($"/fotos/{Ids.Of(1)}/thumb-256", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.Equal(TimeSpan.FromSeconds(600), response.Headers.CacheControl?.MaxAge);
        var target = response.Headers.Location!;
        Assert.EndsWith($"/derivatives/{Ids.Of(1)}/thumb-256.webp", target.AbsolutePath, StringComparison.Ordinal);
        var query = HttpUtility.ParseQueryString(target.Query);
        Assert.Equal("r", query["sp"]);
        var expiry = DateTimeOffset.Parse(query["se"]!, CultureInfo.InvariantCulture);
        Assert.True(expiry <= DateTimeOffset.UtcNow.AddMinutes(15), $"Expiry {expiry:O} is more than 15 minutes away.");
        Assert.True(expiry >= before.AddMinutes(9), $"Expiry {expiry:O} is shorter than the 10-minute cache.");
    }

    [Fact]
    public async Task The_original_redirects_to_a_download_named_after_its_source()
    {
        await using var web = await WebWithPhotoAsync(Ids.Of(2));
        using var http = web.CreateNonRedirectingClient();

        using var response = await http.GetAsync(new Uri($"/fotos/{Ids.Of(2)}/original", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("r", query["sp"]);
        Assert.Equal($"attachment; filename*=UTF-8''Praia%20{Ids.Of(2)[^4..]}.jpg", query["rscd"]);
        Assert.Equal("image/jpeg", query["rsct"]);
    }

    [Theory]
    [InlineData("/fotos/{0}")]
    [InlineData("/fotos/not-an-id")]
    [InlineData("/fotos/{0}/thumb-256")]
    [InlineData("/fotos/{0}/original")]
    [InlineData("/fotos/{1}/poster")]
    public async Task Anything_that_does_not_exist_is_a_404(string path)
    {
        await using var web = await WebWithPhotoAsync(Ids.Of(3));
        using var http = web.CreateNonRedirectingClient();

        // {0} is an id that is not in the catalog; {1} is a photo, which has no poster.
        var url = string.Format(CultureInfo.InvariantCulture, path, Ids.Of(4), Ids.Of(3));
        using var response = await http.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<WebFactory> WebWithPhotoAsync(string id)
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(id, new DateTime(2019, 7, 14, 10, 32, 0)));
        await db.SaveChangesAsync();
        return new WebFactory(catalog.ConnectionStringFor(db));
    }
}
