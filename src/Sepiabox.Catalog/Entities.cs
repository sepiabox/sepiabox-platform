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
