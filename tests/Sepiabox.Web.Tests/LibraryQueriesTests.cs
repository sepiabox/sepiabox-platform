using Microsoft.EntityFrameworkCore;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class LibraryQueriesTests(CatalogFixture catalog)
{
    [Fact]
    public async Task Pages_hold_200_assets_newest_first_with_undated_ones_last()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var start = new DateTime(2020, 1, 1);
        db.Assets.AddRange(Enumerable.Range(1, 201).Select(n => CatalogData.Photo(Ids.Of(n), start.AddDays(n))));
        db.Assets.Add(CatalogData.Photo(Ids.Of(500), null));
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var first = await queries.GetTimelinePageAsync(1);
        var second = await queries.GetTimelinePageAsync(2);

        Assert.Equal(LibraryQueries.PageSize, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Equal(Ids.Of(201), first.Items[0].Id);
        Assert.Equal($"{Ids.Of(201)}/thumb-256.webp", first.Items[0].ThumbnailBlobPath);
        Assert.Equal([Ids.Of(1), Ids.Of(500)], second.Items.Select(i => i.Id));
        Assert.Null(second.Items[^1].CapturedAt);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task An_asset_comes_back_with_its_sources_and_preview()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(9), new DateTime(2019, 7, 14, 10, 32, 0)));
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var asset = await queries.GetAssetAsync(Ids.Of(9));

        Assert.NotNull(asset);
        Assert.Equal($"{Ids.Of(9)}/preview-1280.webp", asset.PreviewBlobPath);
        Assert.Equal("HD Ana", Assert.Single(asset.Sources).Name);
        Assert.Null(await queries.GetAssetAsync(Ids.Of(10)));
    }
}
