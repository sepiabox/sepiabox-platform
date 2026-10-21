using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.SavedPlaces;

namespace Sepiabox.Web.Services;

/// <summary>One of the family's places on the places page (S-014).</summary>
public sealed record SavedPlaceSummary(long Id, string Name, double Latitude, double Longitude, int RadiusM, bool IsHome);

/// <summary>The family's places (S-014, D-083), one short-lived context each.</summary>
public interface ISavedPlaces
{
    /// <summary>The home first, then by name.</summary>
    Task<IReadOnlyList<SavedPlaceSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<SavedPlaceOutcome> CreateAsync(string? name, double latitude, double longitude, int radiusM, bool isHome, CancellationToken cancellationToken = default);

    /// <summary>A null radius keeps the place's own.</summary>
    Task<SavedPlaceOutcome> UpdateAsync(long id, string? name, int? radiusM, bool isHome, CancellationToken cancellationToken = default);

    Task<SavedPlaceOutcome> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary><see cref="SavedPlaceEditor"/> on the catalog.</summary>
public sealed class SavedPlaces(IDbContextFactory<CatalogDbContext> contexts) : ISavedPlaces
{
    public async Task<IReadOnlyList<SavedPlaceSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var places = await db.SavedPlaces.AsNoTracking().ToListAsync(cancellationToken);
        var byName = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        return [.. places
            .OrderByDescending(p => p.IsHome).ThenBy(p => p.Name, byName)
            .Select(p => new SavedPlaceSummary(p.Id, p.Name, p.Location.Y, p.Location.X, p.RadiusM, p.IsHome))];
    }

    public async Task<SavedPlaceOutcome> CreateAsync(
        string? name, double latitude, double longitude, int radiusM, bool isHome, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await new SavedPlaceEditor(db).CreateAsync(name, latitude, longitude, radiusM, isHome, cancellationToken)).Outcome;
    }

    public async Task<SavedPlaceOutcome> UpdateAsync(long id, string? name, int? radiusM, bool isHome, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new SavedPlaceEditor(db).UpdateAsync(id, name, radiusM, isHome, cancellationToken);
    }

    public async Task<SavedPlaceOutcome> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new SavedPlaceEditor(db).DeleteAsync(id, cancellationToken);
    }
}
