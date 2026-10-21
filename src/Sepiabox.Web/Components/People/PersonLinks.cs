using Sepiabox.Catalog.People;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Components.People;

/// <summary>Where a correction on a person sends the browser (S-020): post, redirect, get.</summary>
public static class PersonLinks
{
    public const string NoteParameter = "note";

    /// <summary>
    /// Back to the person with a note saying what happened (<paramref name="done"/> when it was done, the outcome's
    /// name otherwise), or to the people page when the person is gone.
    /// </summary>
    public static async Task<string> AfterAsync(ILibraryQueries library, long personId, CurationOutcome outcome, string done)
    {
        ArgumentNullException.ThrowIfNull(library);
        var note = outcome == CurationOutcome.Done ? done : outcome.ToString();
        return await library.GetPersonAsync(personId) is not null
            ? $"people/{personId}?{NoteParameter}={Uri.EscapeDataString(note)}"
            : "people";
    }
}
