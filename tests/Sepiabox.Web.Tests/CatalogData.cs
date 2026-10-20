using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;

namespace Sepiabox.Web.Tests;

/// <summary>Opens new contexts on one test database, the way the web's factory does.</summary>
internal sealed class TestContexts(string connectionString) : IDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, connectionString);
        return new CatalogDbContext(options.Options);
    }
}

internal static class CatalogData
{
    public static Asset Photo(string id, DateTime? capturedAt, bool withDerivatives = true) => new()
    {
        Id = id,
        Kind = "photo",
        ContentType = "image/jpeg",
        SizeBytes = 1000,
        CapturedAt = capturedAt,
        CameraMake = "Apple",
        CameraModel = "iPhone 15",
        Width = 4000,
        Height = 3000,
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Sources = [new AssetSource { AssetId = id, Type = "local", Name = "HD Ana", Path = $"2019/Praia {id[^4..]}.jpg" }],
        Derivatives = withDerivatives
            ?
            [
                new Derivative { AssetId = id, Name = "thumb-256", BlobPath = $"{id}/thumb-256.webp", Width = 256, Height = 192, ContentType = "image/webp" },
                new Derivative { AssetId = id, Name = "preview-1280", BlobPath = $"{id}/preview-1280.webp", Width = 1280, Height = 960, ContentType = "image/webp" },
            ]
            : [],
    };

    /// <summary>A 3-second camcorder video with its thumbnail and poster, and its 720p version unless <paramref name="withProxy"/> is false.</summary>
    public static Asset Video(string id, DateTime? capturedAt, bool withProxy = true) => new()
    {
        Id = id,
        Kind = "video",
        ContentType = "video/mpeg",
        SizeBytes = 1000,
        CapturedAt = capturedAt,
        Width = 1024,
        Height = 576,
        DurationSeconds = 3,
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Sources = [new AssetSource { AssetId = id, Type = "local", Name = "HD Ana", Path = $"2020/MOV{id[^4..]}.MOD" }],
        Derivatives =
        [
            new Derivative { AssetId = id, Name = "thumb-256", BlobPath = $"{id}/thumb-256.webp", Width = 256, Height = 144, ContentType = "image/webp" },
            new Derivative { AssetId = id, Name = "poster", BlobPath = $"{id}/poster.webp", Width = 1024, Height = 576, ContentType = "image/webp" },
            .. withProxy
                ? new[] { new Derivative { AssetId = id, Name = "proxy-720p", BlobPath = $"{id}/proxy-720p.mp4", Width = 1024, Height = 576, ContentType = "video/mp4" } }
                : [],
        ],
    };
}
