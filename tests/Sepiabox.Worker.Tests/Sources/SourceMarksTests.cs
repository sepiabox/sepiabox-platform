using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Worker.Sources;

namespace Sepiabox.Worker.Tests.Sources;

/// <summary>The removed marks on the sources of a cloud account (S-038, D-115): which source a mark lands on, and when.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SourceMarksTests(CatalogFixture fixture)
{
    private const string Type = "google-drive";
    private const string Name = "ana@example.com (Google Drive)";
    private const string Path = "Fotos/a.jpg";
    private static readonly DateTimeOffset s_modified = new(2020, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime s_now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Marks_the_source_with_the_same_path_time_and_size_and_dates_the_mark_with_the_sync_clock()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await SeedAsync(db, 'a', Path, s_modified, 100);

        var marked = await new SourceMarks(db, new FixedClock(s_now)).MarkRemovedAsync(Type, Name, Path, s_modified, 100, CancellationToken.None);

        Assert.Equal(1, marked);
        Assert.Equal(new DateTimeOffset(s_now), (await db.AssetSources.AsNoTracking().SingleAsync()).RemovedAt);
    }

    [Fact]
    public async Task Leaves_alone_a_source_with_another_path_another_name_another_time_or_another_size()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await SeedAsync(db, 'a', Path, s_modified, 100);
        var marks = new SourceMarks(db, new FixedClock(s_now));

        Assert.Equal(0, await marks.MarkRemovedAsync(Type, Name, "Fotos/b.jpg", s_modified, 100, CancellationToken.None));
        Assert.Equal(0, await marks.MarkRemovedAsync(Type, "bia@example.com (Google Drive)", Path, s_modified, 100, CancellationToken.None));
        Assert.Equal(0, await marks.MarkRemovedAsync(Type, Name, Path, s_modified.AddSeconds(1), 100, CancellationToken.None));
        Assert.Equal(0, await marks.MarkRemovedAsync(Type, Name, Path, s_modified, 101, CancellationToken.None));
        Assert.Equal(0, await marks.MarkRemovedAsync("onedrive", Name, Path, s_modified, 100, CancellationToken.None));
        Assert.Null((await db.AssetSources.AsNoTracking().SingleAsync()).RemovedAt);
    }

    [Fact]
    public async Task Matches_on_the_path_and_time_alone_when_the_size_is_not_known()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await SeedAsync(db, 'a', Path, s_modified, 100);

        Assert.Equal(1, await new SourceMarks(db, new FixedClock(s_now)).MarkRemovedAsync(Type, Name, Path, s_modified, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_second_mark_does_not_move_the_date_of_the_first()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await SeedAsync(db, 'a', Path, s_modified, 100);
        await new SourceMarks(db, new FixedClock(s_now)).MarkRemovedAsync(Type, Name, Path, s_modified, 100, CancellationToken.None);

        var again = await new SourceMarks(db, new FixedClock(s_now.AddDays(3))).MarkRemovedAsync(Type, Name, Path, s_modified, 100, CancellationToken.None);

        Assert.Equal(0, again);
        Assert.Equal(new DateTimeOffset(s_now), (await db.AssetSources.AsNoTracking().SingleAsync()).RemovedAt);
    }

    [Fact]
    public async Task Clearing_removes_the_mark_of_the_matching_source_only()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await SeedAsync(db, 'a', Path, s_modified, 100);
        await SeedAsync(db, 'b', Path, s_modified.AddDays(1), 100);
        await db.AssetSources.ExecuteUpdateAsync(set => set.SetProperty(x => x.RemovedAt, new DateTimeOffset(s_now)));
        var marks = new SourceMarks(db, new FixedClock(s_now));

        Assert.Equal(1, await marks.ClearRemovedAsync(Type, Name, Path, s_modified, 100, CancellationToken.None));
        Assert.Equal(0, await marks.ClearRemovedAsync(Type, Name, Path, s_modified, 100, CancellationToken.None));

        var cleared = await db.AssetSources.AsNoTracking().Where(s => s.RemovedAt == null).Select(s => s.AssetId).ToListAsync();
        Assert.Equal(['a'], cleared.Select(id => id[0]));
    }

    private static async Task SeedAsync(CatalogDbContext db, char id, string path, DateTimeOffset modifiedAt, long size)
    {
        var assetId = new string(id, 64);
        db.Assets.Add(new Asset
        {
            Id = assetId,
            Kind = "photo",
            ContentType = "image/jpeg",
            SizeBytes = size,
            Sources = [new AssetSource { AssetId = assetId, Type = Type, Name = Name, Path = path, ModifiedAt = modifiedAt }],
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
