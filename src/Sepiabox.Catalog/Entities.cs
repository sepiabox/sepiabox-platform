using NetTopologySuite.Geometries;
using Pgvector;

namespace Sepiabox.Catalog;

/// <summary>
/// One photo or video, identified by the SHA-256 of its content (D-014). The library is shared by the
/// family, so there is no owner (D-018).
/// </summary>
public sealed class Asset
{
    public required string Id { get; set; }

    /// <summary><c>photo</c> or <c>video</c>, as in the manifest.</summary>
    public required string Kind { get; set; }

    public long SizeBytes { get; set; }

    public required string ContentType { get; set; }

    /// <summary>Wall-clock time on the camera, without time zone.</summary>
    public DateTime? CapturedAt { get; set; }

    /// <summary>WGS 84 point (SRID 4326): X is longitude, Y is latitude.</summary>
    public Point? Location { get; set; }

    public string? CameraMake { get; set; }

    public string? CameraModel { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public double? DurationSeconds { get; set; }

    /// <summary>
    /// The nearest GeoNames city within 50 km of <see cref="Location"/>, its first-level region and its country,
    /// as GeoNames names them (S-012, D-066); null when the asset has no location or no city is near enough.
    /// </summary>
    public string? PlaceCity { get; set; }

    public string? PlaceRegion { get; set; }

    public string? PlaceCountry { get; set; }

    /// <summary>
    /// The face pipeline version of the asset's stored faces (D-047, S-019); null while its faces were never looked
    /// for. Set with no face rows when faces were looked for and none were found.
    /// </summary>
    public int? FacesVersion { get; set; }

    /// <summary>
    /// Whether Sepiabox keeps the original (S-063, D-088). False while every manifest imported says the original stayed
    /// at its source (index-only); true once any of them says it was uploaded, and then it stays true.
    /// </summary>
    public bool OriginalStored { get; set; } = true;

    /// <summary>
    /// How sharp a photo is (S-027, D-098): the variance of the Laplacian of its grayscale image at 512 px, from its
    /// manifest; null for a video and for a photo not measured. Below <c>Search:BlurryBelow</c> it is blurry.
    /// </summary>
    public double? Sharpness { get; set; }

    /// <summary>The photo's 64-bit difference hash, 16 lower-case hex characters (S-027); photos that look alike differ by a few bits.</summary>
    public string? DHash { get; set; }

    /// <summary>
    /// Set aside by the family on the organize page (S-027): left out of search, the timeline and counts unless the query says
    /// <c>includeHidden</c>. Only a person changes it, never an import.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>When the asset first entered the catalog.</summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>When a manifest last changed it.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public List<AssetSource> Sources { get; set; } = [];

    public List<Derivative> Derivatives { get; set; } = [];
}

/// <summary>One place where the asset's bytes were found, such as a path on a drive.</summary>
public sealed class AssetSource
{
    public long Id { get; set; }

    public required string AssetId { get; set; }

    public required string Type { get; set; }

    public required string Name { get; set; }

    public required string Path { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>The original's address at its source (S-063), when the source gave one; <c>https://</c>.</summary>
    public string? Url { get; set; }

    /// <summary>
    /// When the sync of a cloud source found the file gone from its source (S-038, D-115): deleted, trashed or moved. The source is
    /// kept (sources are only added to, D-032) and marked; the sync clears it when the file comes back.
    /// </summary>
    public DateTimeOffset? RemovedAt { get; set; }
}

/// <summary>A file generated for the web, stored in the <c>derivatives</c> container.</summary>
public sealed class Derivative
{
    public required string AssetId { get; set; }

    /// <summary>Stable name the web asks for, such as <c>thumb-256</c>.</summary>
    public required string Name { get; set; }

    /// <summary>Blob name in the <c>derivatives</c> container: <c>&lt;sha256&gt;/thumb-256.webp</c>.</summary>
    public required string BlobPath { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public required string ContentType { get; set; }
}

/// <summary>Which version (ETag) of each manifest blob was imported, so unchanged ones are skipped.</summary>
public sealed class ManifestImport
{
    public required string BlobName { get; set; }

    public required string ETag { get; set; }

    public DateTimeOffset ImportedAt { get; set; }
}

/// <summary>
/// A vector computed from the asset's image by one model (S-015), such as CLIP ViT-B/32. Vectors are
/// L2-normalized, so cosine distance ranks the most similar images first.
/// </summary>
public sealed class AssetEmbedding
{
    public required string AssetId { get; set; }

    /// <summary>The manifest's key, such as <c>clip-vit-b32</c>.</summary>
    public required string Model { get; set; }

    public required Vector Embedding { get; set; }
}

/// <summary>
/// A populated place from GeoNames' <c>cities500</c> (S-012, D-066), with its first-level region and country
/// names resolved at load time. GeoNames data is CC BY 4.0 (README).
/// </summary>
public sealed class GeoName
{
    public long GeonameId { get; set; }

    public required string Name { get; set; }

    public required string AsciiName { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public required string CountryCode { get; set; }

    public string? CountryName { get; set; }

    public string? Admin1Name { get; set; }

    public long Population { get; set; }

    /// <summary>WGS 84 point (SRID 4326): X is longitude, Y is latitude.</summary>
    public required NetTopologySuite.Geometries.Point Location { get; set; }
}

/// <summary>
/// One person's questions on one day (S-025): how many were asked, against the daily limit, and the tokens the
/// provider billed for them. The person is a SHA-256 of their e-mail address, never the address (D-073).
/// </summary>
public sealed class QuestionUsage
{
    /// <summary>Lower-case hex SHA-256 of the signed-in e-mail address, lower-cased.</summary>
    public required string UserKey { get; set; }

    /// <summary>The day in the library's time zone (<c>Search:TimeZone</c>).</summary>
    public DateOnly Day { get; set; }

    public int Questions { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CacheReadInputTokens { get; set; }

    public long CacheCreationInputTokens { get; set; }
}

/// <summary>
/// Someone whose faces were grouped together (S-019, D-077): unnamed until the family names them (S-020). The library
/// is shared, so a person belongs to no user; <see cref="UserEmail"/> only links a user to themselves (D-018).
/// </summary>
public sealed class Person
{
    public long Id { get; set; }

    /// <summary>Null until someone names the person: the web shows "Unnamed".</summary>
    public string? Name { get; set; }

    /// <summary>The signed-in user who said "this is me" (S-020); null otherwise.</summary>
    public string? UserEmail { get; set; }

    /// <summary>How many faces are assigned to the person (not excluded), of any face version.</summary>
    public int FaceCount { get; set; }

    /// <summary>
    /// The L2-normalized mean of the person's faces of <see cref="FacesVersion"/> only, so it never mixes the
    /// embeddings of two face models; null when the person has no face to average.
    /// </summary>
    public Vector? Centroid { get; set; }

    /// <summary>The face pipeline version <see cref="Centroid"/> was computed from (D-047).</summary>
    public int FacesVersion { get; set; } = 1;

    public List<Face> Faces { get; set; } = [];
}

/// <summary>
/// One face found in an asset (S-018, S-019). <see cref="Box"/> is <c>[left, top, width, height]</c> relative to the
/// asset's upright derivatives, so it is mapped with a derivative's width and height, never the asset's.
/// </summary>
public sealed class Face
{
    public long Id { get; set; }

    public required string AssetId { get; set; }

    public required double[] Box { get; set; }

    public double Score { get; set; }

    /// <summary>SFace's 128 values, L2-normalized (D-047).</summary>
    public required Vector Embedding { get; set; }

    public long? PersonId { get; set; }

    public Person? Person { get; set; }

    /// <summary>One of <see cref="FaceAssignments"/>: who put the face where it is.</summary>
    public required string Assignment { get; set; }
}

/// <summary>How a face got its person: the importer's own choice, the family's, or excluded from every person.</summary>
public static class FaceAssignments
{
    /// <summary>Chosen by the importer by similarity (S-019); the only kind a later import may change.</summary>
    public const string Auto = "auto";

    /// <summary>Put there by someone in the family (S-020); never changed by an import.</summary>
    public const string Manual = "manual";

    /// <summary>
    /// Removed by the family from the person it still points to ("Not this person", S-020): counted for nobody, in no
    /// centroid, kept through re-imports, and listed on that person's page to give back or move.
    /// </summary>
    public const string Excluded = "excluded";

    /// <summary>
    /// Not a face at all, said by the family (S-020): like <see cref="Excluded"/> for counts, centroids and imports, but
    /// shown as such, and still pointing to the person it was found under, so it can be given back.
    /// </summary>
    public const string NotFace = "not_face";

    /// <summary>Whether a face with this assignment counts for its person and is in their centroid.</summary>
    public static bool Counts(string assignment) => assignment is Auto or Manual;
}

/// <summary>A library-wide setting (S-022): a key and its value, such as whether face recognition is on.</summary>
public sealed class Setting
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}

/// <summary>
/// Where a face whose data the family deleted was ("Delete face data" on a person, S-022, D-081): only its box, never
/// its embedding, so a re-import of the asset's manifest, which still lists the face, does not bring it back.
/// </summary>
public sealed class ErasedFace
{
    public long Id { get; set; }

    public required string AssetId { get; set; }

    public required double[] Box { get; set; }

    public DateTimeOffset ErasedAt { get; set; }
}

/// <summary>A record of face data deleted (S-022, D-081): when, by whom, and how much; never whose faces.</summary>
public sealed class FaceDataDeletion
{
    public long Id { get; set; }

    public DateTimeOffset DeletedAt { get; set; }

    /// <summary>The signed-in user who asked, lower-cased.</summary>
    public required string DeletedBy { get; set; }

    /// <summary>One of <see cref="FaceDataDeletionScopes"/>.</summary>
    public required string Scope { get; set; }

    public int Faces { get; set; }

    public int People { get; set; }
}

/// <summary>What a <see cref="FaceDataDeletion"/> deleted.</summary>
public static class FaceDataDeletionScopes
{
    /// <summary>Face recognition turned off: every face and person.</summary>
    public const string Library = "library";

    /// <summary>"Delete face data" on one person: that person and their faces.</summary>
    public const string Person = "person";
}

/// <summary>
/// A place only the family knows (S-014, D-083): "Beach house", "Grandma's". Its name is unique ignoring case; at most
/// one place is the home, a flag rather than a name, so it works in any language (D-050).
/// </summary>
public sealed class SavedPlace
{
    public long Id { get; set; }

    public required string Name { get; set; }

    /// <summary>The centre, a WGS 84 point (SRID 4326): X is longitude, Y is latitude.</summary>
    public required NetTopologySuite.Geometries.Point Location { get; set; }

    /// <summary>How far from <see cref="Location"/> a photo still counts as taken there, in metres.</summary>
    public int RadiusM { get; set; }

    public bool IsHome { get; set; }
}

/// <summary>
/// An occasion (S-023, D-019): photos and videos close together in time and place, such as a wedding or a trip. A
/// table of its own, <c>events</c>, because "how many weddings" counts occasions, not photos. Rebuilt by
/// <c>sepiabox-worker events rebuild</c>; its title is never stored, so it is written in the reader's language (D-050).
/// </summary>
public sealed class Occasion
{
    public long Id { get; set; }

    /// <summary>The camera time of the occasion's first asset (wall clock, no time zone, as <see cref="Asset.CapturedAt"/>).</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>The camera time of its last asset.</summary>
    public DateTime EndsAt { get; set; }

    /// <summary>The mean position of its geotagged assets; null when none has a location.</summary>
    public NetTopologySuite.Geometries.Point? Centroid { get; set; }

    /// <summary>
    /// The city most of its geotagged assets are in, as GeoNames names it (D-066), or their country when no asset has
    /// a city; null when no asset has a place. The title adds the dates to it.
    /// </summary>
    public string? PlaceLabel { get; set; }

    public int AssetCount { get; set; }

    /// <summary>What the occasion was, such as a wedding (S-024); null until it is classified.</summary>
    public string? Type { get; set; }
}

/// <summary>An asset in an occasion; an asset is in at most one (S-023).</summary>
public sealed class OccasionAsset
{
    public long EventId { get; set; }

    public required string AssetId { get; set; }
}

/// <summary>
/// An account of a cloud service the family connected (S-037, D-097): the library is shared, so a connection belongs to
/// no user (D-018). Its token is stored protected, never in clear text; disconnecting deletes the row.
/// </summary>
public sealed class ConnectedSource
{
    public long Id { get; set; }

    /// <summary>One of <see cref="ConnectedSourceTypes"/>.</summary>
    public required string Type { get; set; }

    /// <summary>The account's address at the service, lower-cased; a type has one connection per address.</summary>
    public required string AccountEmail { get; set; }

    /// <summary>The refresh token, protected by ASP.NET Data Protection; only the web reads it (S-037).</summary>
    public required string EncryptedRefreshToken { get; set; }

    /// <summary>One of <see cref="ConnectedSourceStatuses"/>.</summary>
    public required string Status { get; set; }

    public DateTimeOffset ConnectedAt { get; set; }

    /// <summary>When the source was last synchronised (S-038); null until then.</summary>
    public DateTimeOffset? LastSyncAt { get; set; }

    /// <summary>Photos and videos found by the last count, without importing them; null before one.</summary>
    public int? PhotoCount { get; set; }

    public int? VideoCount { get; set; }

    public DateTimeOffset? CountedAt { get; set; }

    /// <summary>
    /// The purpose the refresh token is protected under (S-037, D-097): the web protects it and the sync job unprotects it, both
    /// with the same key ring, so both name this.
    /// </summary>
    public const string RefreshTokenPurpose = "Sepiabox.Web.ConnectedSources.RefreshToken.v1";

    /// <summary>
    /// Where the sync (S-038) is in the account's changes: the token the changes API goes on from. Set when the first scan begins,
    /// before the listing, so what changes meanwhile is seen later. Null before the first sync.
    /// </summary>
    public string? SyncChangesToken { get; set; }

    /// <summary>The page the first scan's listing goes on from; null at the start of a listing and once it is complete.</summary>
    public string? SyncListToken { get; set; }

    /// <summary>True while the first scan lists everything, a few files a run (<c>Sync:MaxFilesPerRun</c>).</summary>
    public bool SyncListing { get; set; }
}

/// <summary>
/// A file of a connected account that the sync has staged (S-038, D-115): its id in the cloud with the path it was given, so a file the
/// changes API reports as removed (which comes with an id and nothing else) is found, and a file that has not changed is not staged again.
/// </summary>
public sealed class DriveFile
{
    public long ConnectedSourceId { get; set; }

    /// <summary>The file's id in the cloud.</summary>
    public required string FileId { get; set; }

    /// <summary>The path of the source this file made: the folders and the name, with <c>/</c>.</summary>
    public required string Path { get; set; }

    /// <summary>The file's modification time in the cloud when it was staged.</summary>
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>The cloud's checksum of the content, when it gives one.</summary>
    public string? Checksum { get; set; }

    /// <summary>
    /// The file's size in bytes when it was staged. With the path and the modification time it tells apart two files of one name in one
    /// folder (a Drive allows that), which the path alone cannot.
    /// </summary>
    public long? SizeBytes { get; set; }
}

/// <summary>The services a <see cref="ConnectedSource"/> can be.</summary>
public static class ConnectedSourceTypes
{
    public const string GoogleDrive = "google-drive";

    /// <summary>A personal Microsoft account's OneDrive, through Microsoft Graph (S-039).</summary>
    public const string OneDrive = "onedrive";

    /// <summary>
    /// The name a cloud account's files have as a source (<c>asset_sources.name</c>, and so in the manifests, for good): the address and
    /// the service, <c>ana@example.com (Google Drive)</c>, since one address may be connected to two services and would otherwise be one source.
    /// </summary>
    public static string SourceName(string type, string accountEmail) => $"{accountEmail} ({ServiceName(type)})";

    /// <summary>The account address in a source name made by <see cref="SourceName"/> for that type; null when it is not one.</summary>
    public static string? AccountOf(string type, string sourceName)
    {
        var suffix = $" ({ServiceName(type)})";
        return sourceName.EndsWith(suffix, StringComparison.Ordinal) && sourceName.Length > suffix.Length ? sourceName[..^suffix.Length] : null;
    }

    private static string ServiceName(string type) => type switch
    {
        GoogleDrive => "Google Drive",
        OneDrive => "OneDrive",
        _ => type,
    };
}

/// <summary>Whether a connection works.</summary>
public static class ConnectedSourceStatuses
{
    public const string Connected = "connected";

    /// <summary>
    /// The service no longer honours the token (<c>invalid_grant</c>: the consent screen in Testing mode expires it after
    /// 7 days, D-024, or it was revoked): the family has to connect again.
    /// </summary>
    public const string Reconnect = "reconnect";
}

/// <summary>
/// A request to export all the library's data (S-040, D-110): made on the settings page, picked up by the export job, which
/// writes <c>exports/&lt;date&gt;-&lt;id&gt;.zip</c> and says so here. The page lists the last ones and gives a one-hour link
/// to each finished zip.
/// </summary>
public sealed class ExportRequest
{
    /// <summary>
    /// Days a zip is kept in <c>exports</c> (D-110): a lifecycle rule deletes it then, and the page stops offering it. A zip with
    /// face data (biometric data) must not outlive an erase for long, and nobody deletes the others by hand.
    /// </summary>
    public const int RetentionDays = 7;

    public long Id { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>The signed-in user who asked, lower-cased.</summary>
    public required string RequestedBy { get; set; }

    /// <summary>Whether the zip carries the faces: their boxes and embeddings (biometric data), which the default leaves out.</summary>
    public bool IncludeFaceData { get; set; }

    /// <summary>One of <see cref="ExportStatuses"/>.</summary>
    public required string Status { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// The zip's blob in the <c>exports</c> container, once it is written; null again once the export job deleted it because
    /// face recognition was turned off and the zip had face data (D-110).
    /// </summary>
    public string? BlobName { get; set; }

    public int? Assets { get; set; }

    public long? SizeBytes { get; set; }

    /// <summary>Why it failed, for the owner to read: no stack, no path.</summary>
    public string? Error { get; set; }
}

/// <summary>The states of an <see cref="ExportRequest"/>.</summary>
public static class ExportStatuses
{
    public const string Requested = "requested";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}
