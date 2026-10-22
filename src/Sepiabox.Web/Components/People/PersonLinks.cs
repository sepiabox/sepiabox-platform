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
    public static async Task<string> AfterAsync(ILibraryQueries library, long personId, CurationOutcome outcome, string done, string? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        var note = outcome == CurationOutcome.Done ? done : outcome.ToString();
        return await library.GetPersonAsync(personId) is not null
            ? $"people/{personId}?{NoteParameter}={Uri.EscapeDataString(note)}{(anchor is null ? string.Empty : "#" + anchor)}"
            : "people";
    }

    /// <summary>The <c>id</c> of a face's card on a person's page (the target of <see cref="AnchorAfter"/>).</summary>
    public static string FaceAnchor(long faceId) => $"face-{faceId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Where the page should be scrolled to after a correction of <paramref name="faceId"/>: the card that followed it in its
    /// list, else the one before it, else nothing (the top). The page is answered with a redirect, which without this starts at
    /// the top, losing the person's place in a long list.
    /// </summary>
    public static string? AnchorAfter(PersonDetail? person, long faceId)
    {
        if (person is null)
        {
            return null;
        }

        foreach (var list in new[] { person.Faces, person.ExcludedFaces })
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Id != faceId)
                {
                    continue;
                }

                return i + 1 < list.Count ? FaceAnchor(list[i + 1].Id) : i > 0 ? FaceAnchor(list[i - 1].Id) : null;
            }
        }

        return null;
    }
}
