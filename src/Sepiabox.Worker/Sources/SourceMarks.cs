using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// The marks a sync keeps on the sources of a cloud account (S-038, D-115): a source whose file is gone from the cloud is marked removed,
/// and the mark is cleared when the file is there again. The source itself stays (sources are only added to, D-032). A source is found by
/// its path <b>and</b> the file's modification time and size, since a cloud can hold two files of one name in one folder and the path alone
/// would mark both.
/// </summary>
public sealed class SourceMarks(CatalogDbContext db, TimeProvider clock)
{
    public Task<int> MarkRemovedAsync(string type, string sourceName, string path, DateTimeOffset modifiedAt, long? size, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return Matching(type, sourceName, path, modifiedAt, size).Where(s => s.RemovedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RemovedAt, now), cancellationToken);
    }

    public Task<int> ClearRemovedAsync(string type, string sourceName, string path, DateTimeOffset modifiedAt, long? size, CancellationToken cancellationToken) =>
        Matching(type, sourceName, path, modifiedAt, size).Where(s => s.RemovedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RemovedAt, (DateTimeOffset?)null), cancellationToken);

    private IQueryable<AssetSource> Matching(string type, string sourceName, string path, DateTimeOffset modifiedAt, long? size)
    {
        var sources = db.AssetSources.Where(s => s.Type == type && s.Name == sourceName && s.Path == path && s.ModifiedAt == modifiedAt);
        return size is { } bytes ? sources.Where(s => db.Assets.Any(a => a.Id == s.AssetId && a.SizeBytes == bytes)) : sources;
    }
}
