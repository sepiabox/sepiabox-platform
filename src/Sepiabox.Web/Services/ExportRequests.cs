using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;

namespace Sepiabox.Web.Services;

/// <summary>Asking for an export of all the library's data and getting it back (S-040, D-110).</summary>
public interface IExportRequests
{
    /// <summary>The last requests, newest first.</summary>
    Task<IReadOnlyList<ExportRequest>> RecentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for an export, as <paramref name="email"/>; false, asking for nothing, while an earlier request is waiting or
    /// being written (one at a time is plenty, and a double click must not make two).
    /// </summary>
    Task<bool> RequestAsync(string email, bool includeFaceData, CancellationToken cancellationToken = default);

    /// <summary>A one-hour link to the finished zip of request <paramref name="id"/>; null when it is not there or not done.</summary>
    Task<Uri?> DownloadAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IExportRequests"/> on the catalog; the zip itself is written by the worker's export job.</summary>
public sealed class ExportRequests(IDbContextFactory<CatalogDbContext> contexts, ISasUrlFactory links, TimeProvider clock) : IExportRequests
{
    /// <summary>How many requests the page lists.</summary>
    public const int HistoryLength = 10;

    public async Task<IReadOnlyList<ExportRequest>> RecentAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.ExportRequests.AsNoTracking().OrderByDescending(e => e.Id).Take(HistoryLength).ToListAsync(cancellationToken);
    }

    public async Task<bool> RequestAsync(string email, bool includeFaceData, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await db.ExportRequests.AnyAsync(e => e.Status == ExportStatuses.Requested || e.Status == ExportStatuses.Running, cancellationToken))
        {
            return false;
        }

        db.ExportRequests.Add(new ExportRequest
        {
            RequestedAt = clock.GetUtcNow(),
            RequestedBy = email.ToLowerInvariant(),
            IncludeFaceData = includeFaceData,
            Status = ExportStatuses.Requested,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Another click, or another computer, asked in the same moment: the unique index lets only one through.
            return false;
        }

        return true;
    }

    /// <summary>When the zip of a finished export is deleted: <see cref="ExportRequest.RetentionDays"/> after it was written.</summary>
    public static DateTimeOffset KeptUntil(ExportRequest request) =>
        (request.FinishedAt ?? request.RequestedAt).AddDays(ExportRequest.RetentionDays);

    /// <summary>Whether the zip's retention has passed.</summary>
    public static bool Expired(ExportRequest request, DateTimeOffset now) => now >= KeptUntil(request);

    public async Task<Uri?> DownloadAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var request = await db.ExportRequests.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        // The lifecycle rule deletes the zip after RetentionDays; until it does, the link would be a 404 from storage.
        return request is { Status: ExportStatuses.Done, BlobName: { } blob } && !Expired(request, clock.GetUtcNow())
            ? await links.ForExportAsync(blob, $"sepiabox-export-{request.RequestedAt:yyyy-MM-dd}.zip", cancellationToken)
            : null;
    }
}
