using System.Text;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Storage;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// What every cloud sync does to put a file in <c>incoming</c> (S-038, S-039, D-107): a new blob name for each transfer, the content written
/// as a staged upload straight from the download, and the metadata given in the commit, within 8 KB, never truncated.
/// </summary>
public sealed class IncomingStager(IBlobStore store)
{
    /// <summary>The most a blob's metadata may be, names and values together (Azure's limit; D-107).</summary>
    public const int MaxMetadataBytes = 8 * 1024;

    /// <summary>
    /// Stages the file: <paramref name="download"/> writes its content to the stream it is given. False, with nothing written, when the
    /// metadata would not fit (a path too long): it is never cut, since a path cut short would be another file's.
    /// </summary>
    public async Task<bool> StageAsync(
        IncomingFile incoming, string contentType, Func<Stream, CancellationToken, Task> download, CancellationToken cancellationToken)
    {
        var metadata = incoming.ToMetadata();
        if (MetadataBytes(metadata) > MaxMetadataBytes)
        {
            return false;
        }

        var name = Guid.NewGuid().ToString("N");
        await using var upload = store.BeginUpload(BlobLayout.IncomingContainer, name);
        await using var sink = new StagedBlockStream(upload);
        await download(sink, cancellationToken);
        await sink.FinishAsync(cancellationToken);
        // The commit makes the blob and gives it its metadata in one call, so the event never finds it bare (D-107).
        await upload.CommitAsync(contentType, BlobStorageClass.Default, metadata, cancellationToken);
        return true;
    }

    /// <summary>Only an absolute <c>https</c> address without credentials within the manifest's limit: the schema refuses any other (D-107).</summary>
    public static string? HttpsOnly(string? url) =>
        url is { Length: > 0 and <= ManifestSource.MaxUrlLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
            ? url
            : null;

    private static int MetadataBytes(IReadOnlyDictionary<string, string> metadata) =>
        metadata.Sum(p => Encoding.UTF8.GetByteCount(p.Key) + Encoding.UTF8.GetByteCount(p.Value));
}

/// <summary>Forgetting what a sync did for one account (<c>--rescan</c>, S-038, S-039): the cursor and the list of staged files.</summary>
public static class CloudRescan
{
    /// <summary>
    /// The next run lists the whole account again and stages every file, so those the processing job skipped are put in <c>incoming</c> again;
    /// files already processed are found by their content and only gain the source they already had. False when no such account is connected.
    /// </summary>
    public static async Task<bool> RescanAsync(Sepiabox.Catalog.CatalogDbContext db, string type, string accountEmail, CancellationToken cancellationToken)
    {
        var email = accountEmail.Trim().ToLowerInvariant();
        var account = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(
            db.ConnectedSources, s => s.Type == type && s.AccountEmail == email, cancellationToken);
        if (account is null)
        {
            return false;
        }

        await Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteDeleteAsync(db.DriveFiles.Where(f => f.ConnectedSourceId == account.Id), cancellationToken);
        foreach (var entry in db.ChangeTracker.Entries<Sepiabox.Catalog.DriveFile>().ToList())
        {
            entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }

        (account.SyncChangesToken, account.SyncListToken, account.SyncListing) = (null, null, false);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

/// <summary>How a cloud file's name and folders become a source's path (S-038, S-039): the same for every cloud service.</summary>
public static class CloudNames
{
    /// <summary>
    /// The file's name with <c>/</c> and <c>\</c> made harmless and an extension from its type when it has none; null when it is neither a
    /// photo nor a video even then.
    /// </summary>
    public static string? FileName(string name, string mimeType)
    {
        var clean = Segment(name);
        if (Sepiabox.Pipeline.Media.MediaTypes.Classify(clean) == Sepiabox.Pipeline.Media.MediaKind.Other)
        {
            clean += MimeExtension(mimeType);
        }

        return clean.Length == 0 || Sepiabox.Pipeline.Media.MediaTypes.Classify(clean) == Sepiabox.Pipeline.Media.MediaKind.Other ? null : clean;
    }

    /// <summary>A folder's name as one segment of a path.</summary>
    public static string Segment(string name) => name.Replace('/', '-').Replace('\\', '-').Trim();

    private static string MimeExtension(string mimeType) => mimeType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/heic" => ".heic",
        "image/heif" => ".heif",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/tiff" => ".tif",
        "video/mp4" => ".mp4",
        "video/quicktime" => ".mov",
        "video/x-msvideo" => ".avi",
        "video/x-matroska" => ".mkv",
        "video/mpeg" => ".mpg",
        _ => string.Empty,
    };
}
