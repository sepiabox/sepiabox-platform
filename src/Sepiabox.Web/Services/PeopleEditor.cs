using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;

namespace Sepiabox.Web.Services;

/// <summary>The family's corrections to people from the person page (S-020), one short-lived context each.</summary>
public interface IPeopleEditor
{
    Task<CurationOutcome> RenameAsync(long personId, string? name, CancellationToken cancellationToken = default);

    Task<CurationOutcome> LinkUserAsync(long personId, string email, CancellationToken cancellationToken = default);

    Task<CurationOutcome> UnlinkUserAsync(long personId, string email, CancellationToken cancellationToken = default);

    Task<CurationOutcome> MergeAsync(long personId, long otherId, bool swap = false, CancellationToken cancellationToken = default);

    Task<CurationOutcome> NotThisPersonAsync(long personId, long faceId, CancellationToken cancellationToken = default);

    Task<CurationOutcome> NotAFaceAsync(long personId, long faceId, CancellationToken cancellationToken = default);

    Task<CurationOutcome> ReturnFaceAsync(long personId, long faceId, CancellationToken cancellationToken = default);

    /// <summary>Moves the face to <paramref name="targetId"/>, or to a new unnamed person when null.</summary>
    Task<CurationOutcome> MoveFaceAsync(long personId, long faceId, long? targetId, CancellationToken cancellationToken = default);
}

/// <summary><see cref="PeopleCuration"/> on the catalog, which takes the same lock as the importer (D-079).</summary>
public sealed class PeopleEditor(IDbContextFactory<CatalogDbContext> contexts) : IPeopleEditor
{
    public Task<CurationOutcome> RenameAsync(long personId, string? name, CancellationToken cancellationToken = default) =>
        WithAsync(c => c.RenameAsync(personId, name, cancellationToken), cancellationToken);

    public Task<CurationOutcome> LinkUserAsync(long personId, string email, CancellationToken cancellationToken = default) =>
        WithAsync(c => c.LinkUserAsync(personId, email, cancellationToken), cancellationToken);

    public Task<CurationOutcome> UnlinkUserAsync(long personId, string email, CancellationToken cancellationToken = default) =>
        WithAsync(c => c.UnlinkUserAsync(personId, email, cancellationToken), cancellationToken);

    public Task<CurationOutcome> MergeAsync(long personId, long otherId, bool swap = false, CancellationToken cancellationToken = default) =>
        WithAsync(c => c.MergeAsync(personId, otherId, swap, cancellationToken), cancellationToken);

    public Task<CurationOutcome> NotThisPersonAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        WithAsync(c => c.NotThisPersonAsync(personId, faceId, cancellationToken), cancellationToken);

    public Task<CurationOutcome> NotAFaceAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        WithAsync(c => c.NotAFaceAsync(personId, faceId, cancellationToken), cancellationToken);

    public Task<CurationOutcome> ReturnFaceAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        WithAsync(c => c.ReturnFaceAsync(personId, faceId, cancellationToken), cancellationToken);

    public Task<CurationOutcome> MoveFaceAsync(long personId, long faceId, long? targetId, CancellationToken cancellationToken = default) =>
        WithAsync(c => c.MoveFaceAsync(personId, faceId, targetId, cancellationToken), cancellationToken);

    private async Task<CurationOutcome> WithAsync(Func<PeopleCuration, Task<CurationOutcome>> change, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await change(new PeopleCuration(db));
    }
}
