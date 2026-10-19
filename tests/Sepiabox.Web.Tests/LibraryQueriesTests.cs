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
        db.Assets.Add(CatalogData.Photo(Ids.Of(500), null, withDerivatives: false));
        await db.SaveChangesAsync();
        var queries = Queries(db);

        var first = await queries.GetTimelinePageAsync(1);
        var second = await queries.GetTimelinePageAsync(2);

        Assert.Equal(LibraryQueries.PageSize, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Equal(Ids.Of(201), first.Items[0].Id);
        Assert.True(first.Items[0].HasThumbnail);
        Assert.Equal([Ids.Of(1), Ids.Of(500)], second.Items.Select(i => i.Id));
        Assert.Null(second.Items[^1].CapturedAt);
        Assert.False(second.Items[^1].HasThumbnail);
        Assert.False(second.HasMore);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public async Task A_page_beyond_the_end_is_empty(int page)
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(1), new DateTime(2020, 1, 1)));
        await db.SaveChangesAsync();

        var result = await Queries(db).GetTimelinePageAsync(page);

        Assert.Empty(result.Items);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task The_timeline_order_has_a_matching_index()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();

        var definition = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_assets_timeline'")
            .SingleAsync();

        Assert.Contains("(captured_at DESC NULLS LAST, id)", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_asset_comes_back_with_its_sources_and_preview()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(9), new DateTime(2019, 7, 14, 10, 32, 0)));
        await db.SaveChangesAsync();
        var queries = Queries(db);

        var asset = await queries.GetAssetAsync(Ids.Of(9));

        Assert.NotNull(asset);
        Assert.Equal(DerivativeNames.Preview, asset.PreviewName);
        Assert.Equal("HD Ana", Assert.Single(asset.Sources).Name);
        Assert.Null(await queries.GetAssetAsync(Ids.Of(10)));
    }

    [Fact]
    public async Task Derivative_and_original_lookups_find_blob_path_file_name_and_type()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(CatalogData.Photo(Ids.Of(11), new DateTime(2019, 7, 14)));
        await db.SaveChangesAsync();
        var queries = Queries(db);

        Assert.Equal($"{Ids.Of(11)}/thumb-256.webp", await queries.GetDerivativeBlobPathAsync(Ids.Of(11), "thumb-256"));
        Assert.Null(await queries.GetDerivativeBlobPathAsync(Ids.Of(11), "poster"));
        Assert.Equal(new OriginalFile($"Praia {Ids.Of(11)[^4..]}.jpg", "image/jpeg"), await queries.GetOriginalAsync(Ids.Of(11)));
        Assert.Null(await queries.GetOriginalAsync(Ids.Of(12)));
    }

    private LibraryQueries Queries(Catalog.CatalogDbContext db) => new(new TestContexts(catalog.ConnectionStringFor(db)));
}
