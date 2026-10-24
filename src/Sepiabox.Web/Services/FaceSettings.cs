using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;

namespace Sepiabox.Web.Services;

/// <summary>What the settings page shows about face recognition (S-022).</summary>
/// <param name="State">Whether it is on, and how far the last erase reached the stored manifests.</param>
/// <param name="History">The last deletions of face data, newest first.</param>
public sealed record FaceSettingsView(FaceRecognitionState State, IReadOnlyList<FaceDataDeletion> History);

/// <summary>What "Ignore the unnamed people with few faces" would delete (#107).</summary>
/// <param name="People">How many unnamed people with one or two faces there are.</param>
/// <param name="Crops">The best face of the first of them, cropped, up to the number asked for.</param>
public sealed record FewFacesPreview(int People, IReadOnlyList<FaceCrop> Crops);

/// <summary>The library's face recognition switch and face data deletions (S-022, D-081), one short-lived context each.</summary>
public interface IFaceSettings
{
    Task<FaceSettingsView> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns recognition off and deletes every face and person, recorded as done by <paramref name="email"/>.</summary>
    Task TurnOffAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>False, changing nothing, while the stored manifests do not all carry the last erase yet.</summary>
    Task<bool> TurnOnAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes the person and their faces; false when the person is not there.</summary>
    Task<bool> DeletePersonAsync(long personId, string email, CancellationToken cancellationToken = default);

    /// <summary>"Ignore this face" (<paramref name="faceId"/>) or "Ignore the unnamed faces in this photo" (#107); false when nothing was ignored.</summary>
    Task<bool> IgnoreFacesAsync(string assetId, long? faceId, string email, CancellationToken cancellationToken = default);

    /// <summary>"Ignore this person": an unnamed one only; false when the person is not there or is named.</summary>
    Task<bool> IgnorePersonAsync(long personId, string email, CancellationToken cancellationToken = default);

    /// <summary>How many unnamed people have at most <see cref="FaceRecognition.FewFaces"/> faces, for the bulk action.</summary>
    Task<int> CountUnnamedWithFewFacesAsync(CancellationToken cancellationToken = default);

    /// <summary>The faces the bulk action would delete, for its confirmation: how many people, and the first <paramref name="take"/> best faces cropped.</summary>
    Task<FewFacesPreview> PreviewUnnamedWithFewFacesAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Ignores the unnamed people with few faces, only when exactly <paramref name="expected"/> match.</summary>
    Task<IgnoreResult> IgnoreUnnamedPeopleAsync(int expected, string email, CancellationToken cancellationToken = default);
}

/// <summary><see cref="FaceRecognition"/> on the catalog, under the importer's people lock (D-077).</summary>
public sealed class FaceSettings(IDbContextFactory<CatalogDbContext> contexts, TimeProvider clock) : IFaceSettings
{
    /// <summary>How many deletions the settings page lists.</summary>
    public const int HistoryLength = 20;

    public async Task<FaceSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return new FaceSettingsView(
            await FaceRecognition.ReadAsync(db, cancellationToken),
            await FaceRecognition.HistoryAsync(db, HistoryLength, cancellationToken));
    }

    public async Task TurnOffAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await new FaceRecognition(db, clock).TurnOffAsync(email, cancellationToken);
    }

    public async Task<bool> TurnOnAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new FaceRecognition(db, clock).TurnOnAsync(cancellationToken);
    }

    public async Task<bool> DeletePersonAsync(long personId, string email, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new FaceRecognition(db, clock).DeletePersonAsync(personId, email, cancellationToken) is not null;
    }

    public async Task<bool> IgnoreFacesAsync(string assetId, long? faceId, string email, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new FaceRecognition(db, clock).IgnoreFacesAsync(assetId, faceId, email, cancellationToken) is not null;
    }

    public async Task<bool> IgnorePersonAsync(long personId, string email, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new FaceRecognition(db, clock).IgnorePersonAsync(personId, email, cancellationToken) is not null;
    }

    public async Task<int> CountUnnamedWithFewFacesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await FaceRecognition.UnnamedWithFewFaces(db, FaceRecognition.FewFaces).CountAsync(cancellationToken);
    }

    public async Task<IgnoreResult> IgnoreUnnamedPeopleAsync(int expected, string email, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new FaceRecognition(db, clock).IgnoreUnnamedPeopleAsync(FaceRecognition.FewFaces, expected, email, cancellationToken);
    }

    public async Task<FewFacesPreview> PreviewUnnamedWithFewFacesAsync(int take, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var people = FaceRecognition.UnnamedWithFewFaces(db, FaceRecognition.FewFaces);
        var count = await people.CountAsync(cancellationToken);
        var ids = await people.OrderBy(p => p.Id).Select(p => p.Id).Take(take).ToListAsync(cancellationToken);
        var faces = await db.Faces.AsNoTracking().Where(f => f.PersonId != null && ids.Contains(f.PersonId.Value))
            .Select(f => new { PersonId = f.PersonId!.Value, f.AssetId, f.Box, f.Score }).ToListAsync(cancellationToken);
        var chosen = ids.Select(id => faces.Where(f => f.PersonId == id).OrderByDescending(f => f.Score).FirstOrDefault()).Where(f => f is not null).Select(f => f!).ToList();
        var assetIds = chosen.Select(f => f.AssetId).Distinct().ToList();
        var thumbnails = await db.Derivatives.AsNoTracking()
            .Where(d => assetIds.Contains(d.AssetId) && d.Name == DerivativeNames.Thumbnail && d.Width > 0 && d.Height > 0)
            .ToDictionaryAsync(d => d.AssetId, d => (d.Width, d.Height), StringComparer.Ordinal, cancellationToken);
        var crops = chosen
            .Where(f => f.Box is { Length: 4 } && thumbnails.ContainsKey(f.AssetId))
            .Select(f => new FaceCrop(f.AssetId, f.Box, thumbnails[f.AssetId].Width, thumbnails[f.AssetId].Height))
            .ToList();
        return new FewFacesPreview(count, crops);
    }
}
