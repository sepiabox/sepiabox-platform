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

    public DbSet<AssetEmbedding> AssetEmbeddings => Set<AssetEmbedding>();

    public DbSet<GeoName> GeoNames => Set<GeoName>();

    public DbSet<QuestionUsage> QuestionUsage => Set<QuestionUsage>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Face> Faces => Set<Face>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<ErasedFace> ErasedFaces => Set<ErasedFace>();

    public DbSet<FaceDataDeletion> FaceDataDeletions => Set<FaceDataDeletion>();

    public DbSet<SavedPlace> SavedPlaces => Set<SavedPlace>();

    /// <summary>The one place that knows how the catalog connects, shared by the worker, tests and tooling.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite().UseVector())
            .UseSnakeCaseNamingConvention();

    /// <summary>
    /// Same as <see cref="Configure(DbContextOptionsBuilder, string)"/>, on a data source that already exists
    /// (the web's, registered by Aspire), so EF and the health check share one connection pool. The data
    /// source must have NetTopologySuite and pgvector enabled (<see cref="ConfigureDataSource"/>).
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.UseNetTopologySuite().UseVector())
            .UseSnakeCaseNamingConvention();

    /// <summary>The type mappings a data source for this catalog needs: PostGIS points and pgvector vectors.</summary>
    public static NpgsqlDataSourceBuilder ConfigureDataSource(NpgsqlDataSourceBuilder dataSource)
    {
        dataSource.UseNetTopologySuite();
        dataSource.UseVector();
        return dataSource;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.ToTable("assets");
            asset.Property(a => a.Id).HasMaxLength(64);
            asset.Property(a => a.Kind).HasMaxLength(16);
            // True for every asset before index-only mode (S-063). Never generated, as belt and braces: a column with a
            // default and a CLR default of false is the case where EF may leave the value out of the insert.
            asset.Property(a => a.OriginalStored).HasDefaultValue(true).ValueGeneratedNever();
            // Camera clocks have no time zone, so the value is stored exactly as read.
            asset.Property(a => a.CapturedAt).HasColumnType("timestamp without time zone");
            asset.Property(a => a.Location).HasColumnType("geography (Point, 4326)");
            // Saved places and distances (S-014): ST_DWithin on a place's centre walks this index (D-083).
            asset.HasIndex(a => a.Location).HasDatabaseName("ix_assets_location").HasMethod("gist");
            asset.Property(a => a.PlaceCity).HasMaxLength(200);
            asset.Property(a => a.PlaceRegion).HasMaxLength(200);
            asset.Property(a => a.PlaceCountry).HasMaxLength(200);
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

        modelBuilder.Entity<AssetEmbedding>(embedding =>
        {
            embedding.ToTable("asset_embeddings");
            embedding.HasKey(e => new { e.AssetId, e.Model });
            embedding.Property(e => e.Model).HasMaxLength(32);
            embedding.Property(e => e.Embedding).HasColumnType("vector(512)");
            embedding.HasOne<Asset>().WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Cascade);
            // Approximate nearest neighbours by cosine distance, for "parecidas com esta" and text search (S-016, S-017).
            embedding.HasIndex(e => e.Embedding)
                .HasDatabaseName("ix_asset_embeddings_embedding")
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });

        modelBuilder.Entity<GeoName>(geoName =>
        {
            geoName.ToTable("geonames");
            geoName.HasKey(g => g.GeonameId);
            // GeoNames' own ids: never generated here, so a load replaces rows with the same ids.
            geoName.Property(g => g.GeonameId).ValueGeneratedNever();
            geoName.Property(g => g.Name).HasMaxLength(200);
            geoName.Property(g => g.AsciiName).HasMaxLength(200);
            geoName.Property(g => g.CountryCode).HasMaxLength(2);
            geoName.Property(g => g.CountryName).HasMaxLength(200);
            geoName.Property(g => g.Admin1Name).HasColumnName("admin1_name").HasMaxLength(200);
            geoName.Property(g => g.Location).HasColumnType("geography (Point, 4326)");
            // Nearest-city lookups: ST_DWithin and the <-> distance order both use a GiST index (S-012).
            geoName.HasIndex(g => g.Location).HasDatabaseName("ix_geonames_location").HasMethod("gist");
        });

        modelBuilder.Entity<Person>(person =>
        {
            person.ToTable("people");
            person.Property(p => p.Name).HasMaxLength(200);
            person.Property(p => p.UserEmail).HasMaxLength(320);
            // One person per user (S-020); addresses are stored lower-cased. Names are unique ignoring case through an
            // index on lower(name), which the PeopleUnique migration creates in SQL.
            person.HasIndex(p => p.UserEmail).IsUnique().HasDatabaseName("ix_people_user_email");
            person.Property(p => p.Centroid).HasColumnType("vector(128)");
            // The people page lists the most photographed first (S-019).
            person.HasIndex(p => new { p.FaceCount, p.Id })
                .HasDatabaseName("ix_people_face_count")
                .IsDescending(true, false);
            // The nearest centroid for each new face, by cosine distance (D-016's 0.40 similarity).
            person.HasIndex(p => p.Centroid)
                .HasDatabaseName("ix_people_centroid")
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
            person.HasMany(p => p.Faces).WithOne(f => f.Person).HasForeignKey(f => f.PersonId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Face>(face =>
        {
            face.ToTable("faces");
            face.Property(f => f.AssetId).HasMaxLength(64);
            face.Property(f => f.Box).HasColumnType("double precision[]");
            face.Property(f => f.Embedding).HasColumnType("vector(128)");
            face.Property(f => f.Assignment).HasMaxLength(16);
            face.ToTable(t => t.HasCheckConstraint("ck_faces_assignment", "assignment IN ('auto', 'manual', 'excluded', 'not_face')"));
            face.HasOne<Asset>().WithMany().HasForeignKey(f => f.AssetId).OnDelete(DeleteBehavior.Cascade);
            face.HasIndex(f => f.AssetId);
            face.HasIndex(f => f.PersonId);
        });

        modelBuilder.Entity<SavedPlace>(place =>
        {
            place.ToTable("saved_places");
            place.Property(p => p.Name).HasMaxLength(200);
            place.Property(p => p.Location).HasColumnType("geography (Point, 4326)");
            // Names are unique ignoring case through an index on lower(name), which the SavedPlaces migration creates in
            // SQL; at most one home (S-014).
            place.HasIndex(p => p.IsHome).IsUnique().HasFilter("is_home").HasDatabaseName("ix_saved_places_home");
            place.ToTable(t => t.HasCheckConstraint("ck_saved_places_radius_m", "radius_m BETWEEN 10 AND 50000"));
        });

        modelBuilder.Entity<Setting>(setting =>
        {
            setting.ToTable("settings");
            setting.HasKey(x => x.Key);
            setting.Property(x => x.Key).HasMaxLength(64);
            // Unbounded: the faces sweep's checkpoint holds a storage continuation token (D-081).
            setting.Property(x => x.Value).HasColumnType("text");
        });

        modelBuilder.Entity<ErasedFace>(erased =>
        {
            erased.ToTable("erased_faces");
            erased.Property(f => f.AssetId).HasMaxLength(64);
            erased.Property(f => f.Box).HasColumnType("double precision[]");
            erased.HasOne<Asset>().WithMany().HasForeignKey(f => f.AssetId).OnDelete(DeleteBehavior.Cascade);
            erased.HasIndex(f => f.AssetId);
        });

        modelBuilder.Entity<FaceDataDeletion>(deletion =>
        {
            deletion.ToTable("face_data_deletions");
            deletion.Property(d => d.DeletedBy).HasMaxLength(320);
            deletion.Property(d => d.Scope).HasMaxLength(16);
            deletion.ToTable(t => t.HasCheckConstraint("ck_face_data_deletions_scope", "scope IN ('library', 'person')"));
        });

        modelBuilder.Entity<QuestionUsage>(usage =>
        {
            usage.ToTable("question_usage");
            usage.HasKey(u => new { u.UserKey, u.Day });
            usage.Property(u => u.UserKey).HasMaxLength(64);
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
