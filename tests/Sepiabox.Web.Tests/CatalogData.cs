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
    public static Asset Photo(string id, DateTime? capturedAt) => new()
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
        Sources = [new AssetSource { AssetId = id, Type = "local", Name = "HD Ana", Path = $"2019/{id[..8]}.jpg" }],
        Derivatives =
        [
            new Derivative { AssetId = id, Name = "thumb-256", BlobPath = $"{id}/thumb-256.webp", Width = 256, Height = 192, ContentType = "image/webp" },
            new Derivative { AssetId = id, Name = "preview-1280", BlobPath = $"{id}/preview-1280.webp", Width = 1280, Height = 960, ContentType = "image/webp" },
        ],
    };
}
