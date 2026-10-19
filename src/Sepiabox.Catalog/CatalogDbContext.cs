using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Sepiabox.Catalog;

/// <summary>The PostgreSQL catalog: the source of truth for search (D-003, D-013).</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<AssetSource> AssetSources => Set<AssetSource>();

    public DbSet<Derivative> Derivatives => Set<Derivative>();

    public DbSet<ManifestImport> ManifestImports => Set<ManifestImport>();

    /// <summary>The one place that knows how the catalog connects, shared by the worker, tests and tooling.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention();

    /// <summary>
    /// Same as <see cref="Configure(DbContextOptionsBuilder, string)"/>, on a data source that already exists
    /// (the web's, registered by Aspire), so EF and the health check share one connection pool. The data
    /// source must have NetTopologySuite enabled.
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.ToTable("assets");
            asset.Property(a => a.Id).HasMaxLength(64);
            asset.Property(a => a.Kind).HasMaxLength(16);
            // Camera clocks have no time zone, so the value is stored exactly as read.
            asset.Property(a => a.CapturedAt).HasColumnType("timestamp without time zone");
            asset.Property(a => a.Location).HasColumnType("geography (Point, 4326)");
            // The timeline's order (newest first, undated last, then id), so a page is an index range scan.
            asset.HasIndex(a => new { a.CapturedAt, a.Id })
                .HasDatabaseName("ix_assets_timeline")
                .IsDescending(true, false)
                .HasNullSortOrder(NullSortOrder.NullsLast, NullSortOrder.NullsLast);
            asset.HasMany(a => a.Sources).WithOne().HasForeignKey(s => s.AssetId);
            asset.HasMany(a => a.Derivatives).WithOne().HasForeignKey(d => d.AssetId);
        });

        modelBuilder.Entity<AssetSource>(source =>
        {
            source.ToTable("asset_sources");
            source.HasIndex(s => new { s.AssetId, s.Type, s.Name, s.Path }).IsUnique();
        });

        modelBuilder.Entity<Derivative>(derivative =>
        {
            derivative.ToTable("derivatives");
            derivative.HasKey(d => new { d.AssetId, d.Name });
        });

        modelBuilder.Entity<ManifestImport>(import =>
        {
            import.ToTable("manifest_imports");
            import.HasKey(i => i.BlobName);
            import.Property(i => i.ETag).HasColumnName("etag");
        });
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without a running database.</summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, "Host=localhost;Database=catalog");
        return new CatalogDbContext(options.Options);
    }
}
