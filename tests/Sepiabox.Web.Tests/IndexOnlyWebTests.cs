using System.Net;
using System.Text.Json;
using Sepiabox.Catalog;

namespace Sepiabox.Web.Tests;

/// <summary>An index-only asset on a real catalog (S-064): the page, the larger derivatives, and the download that is a 404.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class IndexOnlyWebTests(CatalogFixture catalog)
{
    private const string Url = "https://photos.google.com/photo/AF1Qip-example";

    [Fact]
    public async Task The_page_of_an_index_only_photo_shows_the_large_preview_and_the_link_and_the_download_is_a_404()
    {
        await using var web = await WebWithAsync(IndexOnlyPhoto(Ids.Of(1)));
        using var http = web.CreateSignedInClient(followRedirects: false);

        var html = await http.GetStringAsync(new Uri($"/photos/{Ids.Of(1)}", UriKind.Relative));
        using var preview = await http.GetAsync(new Uri($"/photos/{Ids.Of(1)}/preview-2048", UriKind.Relative));
        using var download = await http.GetAsync(new Uri($"/photos/{Ids.Of(1)}/original", UriKind.Relative));

        Assert.Contains($"src=\"photos/{Ids.Of(1)}/preview-2048\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{Url}\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Download the original", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/original", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, preview.StatusCode);
        Assert.EndsWith($"/derivatives/{Ids.Of(1)}/preview-2048.webp", preview.Headers.Location!.AbsolutePath, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Null(download.Headers.Location);
        using var body = JsonDocument.Parse(await download.Content.ReadAsStringAsync());
        Assert.Equal("Sepiabox keeps no copy of this original: it stays at its source.", body.RootElement.GetProperty("detail").GetString());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task The_page_of_an_index_only_video_plays_the_1080p_version()
    {
        await using var web = await WebWithAsync(IndexOnlyVideo(Ids.Of(2)));
        using var http = web.CreateSignedInClient(followRedirects: false);

        var html = await http.GetStringAsync(new Uri($"/photos/{Ids.Of(2)}", UriKind.Relative));
        using var proxy = await http.GetAsync(new Uri($"/photos/{Ids.Of(2)}/proxy-1080p", UriKind.Relative));

        Assert.Contains($"src=\"photos/{Ids.Of(2)}/proxy-1080p\"", html, StringComparison.Ordinal);
        Assert.Contains($"poster=\"photos/{Ids.Of(2)}/poster\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, proxy.StatusCode);
        Assert.EndsWith($"/derivatives/{Ids.Of(2)}/proxy-1080p.mp4", proxy.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_asset_with_a_stored_original_keeps_its_download_and_shows_the_larger_preview_when_it_has_both()
    {
        var photo = CatalogData.Photo(Ids.Of(3), new DateTime(2019, 7, 14, 10, 32, 0));
        photo.Derivatives.Add(new Derivative
        {
            AssetId = photo.Id,
            Name = "preview-2048",
            BlobPath = $"{photo.Id}/preview-2048.webp",
            Width = 2048,
            Height = 1536,
            ContentType = "image/webp",
        });
        photo.Sources.Add(new AssetSource { AssetId = photo.Id, Type = "google-takeout", Name = "Takeout 2026", Path = "a.jpg", Url = Url });
        await using var web = await WebWithAsync(photo);
        using var http = web.CreateSignedInClient(followRedirects: false);

        var html = await http.GetStringAsync(new Uri($"/photos/{Ids.Of(3)}", UriKind.Relative));
        using var download = await http.GetAsync(new Uri($"/photos/{Ids.Of(3)}/original", UriKind.Relative));

        Assert.Contains($"src=\"photos/{Ids.Of(3)}/preview-2048\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("preview-1280", html, StringComparison.Ordinal);
        Assert.Contains("Download the original", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{Url}\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, download.StatusCode);
    }

    [Fact]
    public async Task A_hand_edited_catalog_row_with_a_script_address_is_never_a_link()
    {
        var photo = IndexOnlyPhoto(Ids.Of(4));
        photo.Sources[0].Url = "javascript:alert(1)";
        await using var web = await WebWithAsync(photo);
        using var http = web.CreateSignedInClient();

        var html = await http.GetStringAsync(new Uri($"/photos/{Ids.Of(4)}", UriKind.Relative));

        Assert.DoesNotContain("javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Original in Takeout 2026; it can&#x27;t be opened from here.", html.Replace("&#39;", "&#x27;"), StringComparison.Ordinal);
    }

    private static Asset IndexOnlyPhoto(string id)
    {
        var photo = CatalogData.Photo(id, new DateTime(2019, 7, 14, 10, 32, 0));
        photo.OriginalStored = false;
        photo.Derivatives.RemoveAll(d => d.Name == "preview-1280");
        photo.Derivatives.Add(new Derivative
        {
            AssetId = id,
            Name = "preview-2048",
            BlobPath = $"{id}/preview-2048.webp",
            Width = 2048,
            Height = 1536,
            ContentType = "image/webp",
        });
        photo.Sources.Clear();
        photo.Sources.Add(new AssetSource { AssetId = id, Type = "google-takeout", Name = "Takeout 2026", Path = "Google Photos/IMG_0001.jpg", Url = Url });
        return photo;
    }

    private static Asset IndexOnlyVideo(string id)
    {
        var video = CatalogData.Video(id, new DateTime(2020, 8, 9));
        video.OriginalStored = false;
        video.Derivatives.RemoveAll(d => d.Name == "proxy-720p");
        video.Derivatives.Add(new Derivative
        {
            AssetId = id,
            Name = "proxy-1080p",
            BlobPath = $"{id}/proxy-1080p.mp4",
            Width = 1920,
            Height = 1080,
            ContentType = "video/mp4",
        });
        return video;
    }

    private async Task<WebFactory> WebWithAsync(Asset asset)
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return new WebFactory(catalog.ConnectionStringFor(db));
    }
}
