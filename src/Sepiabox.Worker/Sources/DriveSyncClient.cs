namespace Sepiabox.Worker.Sources;

/// <summary>A photo or video of a Google Drive, as the sync needs it (S-038).</summary>
/// <param name="Id">The file's id in Drive.</param>
/// <param name="Name">The file's name.</param>
/// <param name="MimeType"><c>image/*</c> or <c>video/*</c>.</param>
/// <param name="Size">The size in bytes, when Drive says.</param>
/// <param name="ModifiedTime">When the file was last modified in Drive.</param>
/// <param name="Md5">Drive's checksum of the content, when it gives one.</param>
/// <param name="Parents">The ids of the folders it is in.</param>
/// <param name="WebViewLink">The address that opens the file in Drive.</param>
public sealed record DriveFileInfo(
    string Id, string Name, string MimeType, long? Size, DateTimeOffset ModifiedTime, string? Md5, IReadOnlyList<string> Parents, string? WebViewLink);

/// <summary>One page of the listing of the account's photos and videos.</summary>
public sealed record DriveListPage(IReadOnlyList<DriveFileInfo> Files, string? NextPageToken);

/// <summary>A change to the account: a file that was added or changed, or one that is gone (deleted, or in the trash).</summary>
/// <param name="FileId">The file's id.</param>
/// <param name="Removed">True when the file is gone; <paramref name="File"/> is then null.</param>
/// <param name="File">The file, when it is a photo or video that is there.</param>
public sealed record DriveChange(string FileId, bool Removed, DriveFileInfo? File);

/// <summary>One page of changes: the next page, or, on the last, the token to ask from next time.</summary>
public sealed record DriveChangesPage(IReadOnlyList<DriveChange> Changes, string? NextPageToken, string? NewStartPageToken);

/// <summary>A folder, for the path of the files in it.</summary>
/// <param name="Name">The folder's name.</param>
/// <param name="ParentId">The folder it is in; null for the top of the Drive, which is not part of a path.</param>
public sealed record DriveFolder(string Name, string? ParentId);

/// <summary>
/// What the sync job asks of a Drive (S-038, D-051): behind an interface so that tests use a fake and never reach Google. Every
/// call takes the plain refresh token and throws <see cref="Sepiabox.Catalog.CloudAuthorizationException"/> when Google no longer honours it.
/// </summary>
public interface IDriveSyncClient
{
    /// <summary>The token to ask the changes from, as of now.</summary>
    Task<string> GetStartPageTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>A page of the photos and videos that are not in the trash; <paramref name="pageToken"/> null for the first.</summary>
    Task<DriveListPage> ListMediaAsync(string refreshToken, string? pageToken, CancellationToken cancellationToken = default);

    /// <summary>A page of the changes since <paramref name="pageToken"/>; the files in it are photos and videos only.</summary>
    Task<DriveChangesPage> ListChangesAsync(string refreshToken, string pageToken, CancellationToken cancellationToken = default);

    /// <summary>The folder, or null when it is not there or the account cannot see it.</summary>
    Task<DriveFolder?> GetFolderAsync(string refreshToken, string folderId, CancellationToken cancellationToken = default);

    /// <summary>Writes the file's content to <paramref name="destination"/>, a piece at a time.</summary>
    Task DownloadAsync(string refreshToken, string fileId, Stream destination, CancellationToken cancellationToken = default);
}
