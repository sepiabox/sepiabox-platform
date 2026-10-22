using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>Opens new contexts on one test database, the way the web's factory does.</summary>
internal sealed class TestContexts(string connectionString, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null) : IDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, connectionString);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

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

    /// <summary>A 3-second HD camcorder video with its thumbnail and poster, and its 720p version unless <paramref name="withProxy"/> is false.</summary>
    public static Asset Video(string id, DateTime? capturedAt, bool withProxy = true) => new()
    {
        Id = id,
        Kind = AssetKinds.Video,
        ContentType = "video/mpeg",
        SizeBytes = 1000,
        CapturedAt = capturedAt,
        Width = 1920,
        Height = 1080,
        DurationSeconds = 3,
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Sources = [new AssetSource { AssetId = id, Type = "local", Name = "HD Ana", Path = $"2020/MOV{id[^4..]}.MOD" }],
        Derivatives =
        [
            new Derivative { AssetId = id, Name = DerivativeNames.Thumbnail, BlobPath = $"{id}/thumb-256.webp", Width = 256, Height = 144, ContentType = "image/webp" },
            new Derivative { AssetId = id, Name = DerivativeNames.Poster, BlobPath = $"{id}/poster.webp", Width = 1920, Height = 1080, ContentType = "image/webp" },
            .. withProxy
                ? new[] { new Derivative { AssetId = id, Name = DerivativeNames.Proxy, BlobPath = $"{id}/proxy-720p.mp4", Width = 1280, Height = 720, ContentType = "video/mp4" } }
                : [],
        ],
    };
}
