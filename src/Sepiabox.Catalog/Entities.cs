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
