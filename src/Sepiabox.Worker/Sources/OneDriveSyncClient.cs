namespace Sepiabox.Worker.Sources;

/// <summary>
/// The tokens a refresh gives (S-039). Microsoft's refresh tokens roll: each refresh may return a new one, and the account keeps working
/// only if the newest is kept (it lasts 90 days from its issue), so a sync that refreshes must store what it gets back.
/// </summary>
/// <param name="AccessToken">For this run's calls to Graph; never stored.</param>
/// <param name="RefreshToken">The refresh token to keep from now on; null when Microsoft sent none (the one used stays).</param>
public sealed record OneDriveToken(string AccessToken, string? RefreshToken);

/// <summary>A photo or video of a OneDrive, as the sync needs it (S-039).</summary>
/// <param name="Id">The item's id, which is how a delta tracks it.</param>
/// <param name="Name">The file's name.</param>
/// <param name="MimeType"><c>image/*</c> or <c>video/*</c>.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="ModifiedTime">When the file was last modified in OneDrive.</param>
/// <param name="Hash">OneDrive's hash of the content (SHA-1 or quickXor), when it gives one.</param>
/// <param name="ParentId">The folder's id; a delta does not give the path of an item, only this.</param>
/// <param name="WebUrl">The address that opens the file in OneDrive.</param>
public sealed record OneDriveItem(string Id, string Name, string MimeType, long? Size, DateTimeOffset ModifiedTime, string? Hash, string? ParentId, string? WebUrl);

/// <summary>One entry of a delta: a photo or video that is there, or an item that is gone.</summary>
public sealed record OneDriveChange(string Id, bool Removed, OneDriveItem? File);

/// <summary>
/// One page of a delta: the link to the next page, or, on the last, the delta link to ask from next time. Both are addresses Graph made
/// and hold a state token of its own: kept as they are, never built or logged.
/// </summary>
public sealed record OneDriveDeltaPage(IReadOnlyList<OneDriveChange> Changes, string? NextLink, string? DeltaLink);

/// <summary>A folder, for the path of the files in it; <paramref name="ParentId"/> is null for the top of the OneDrive.</summary>
public sealed record OneDriveFolder(string Name, string? ParentId);

/// <summary>
/// What the sync job asks of a OneDrive through Microsoft Graph (S-039, D-051): behind an interface so that tests use a fake and never reach
/// Microsoft. A refresh Microsoft refuses (<c>invalid_grant</c>) throws <see cref="Sepiabox.Catalog.CloudAuthorizationException"/>.
/// </summary>
public interface IOneDriveSyncClient
{
    /// <summary>Exchanges the refresh token for an access token, and for a newer refresh token when Microsoft sends one.</summary>
    Task<OneDriveToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>One page of the delta of the root: from the start when <paramref name="link"/> is null, otherwise from that next or delta link.</summary>
    Task<OneDriveDeltaPage> DeltaAsync(string accessToken, string? link, CancellationToken cancellationToken = default);

    /// <summary>The folder, or null when it is not there.</summary>
    Task<OneDriveFolder?> GetFolderAsync(string accessToken, string itemId, CancellationToken cancellationToken = default);

    /// <summary>Writes the file's content to <paramref name="destination"/>, a piece at a time.</summary>
    Task DownloadAsync(string accessToken, string itemId, Stream destination, CancellationToken cancellationToken = default);
}
