using Microsoft.Extensions.Localization;

namespace Sepiabox.Web.Services;

/// <summary>
/// How the detail page speaks of a source (S-064): a link to the original at its source when the manifest gave an
/// address, and, for an original Sepiabox does not keep (S-063, D-088), a line saying where it is. The wording of each kind
/// of source comes from one map (type to resource key), so the export stories (S-046 to S-049) and the cloud ones
/// (S-038, S-039) only add entries.
/// </summary>
public static class SourceLinks
{
    public const string LocalType = "local";

    /// <summary>The type of a source found in a connected Google Drive (S-037, S-038).</summary>
    public const string GoogleDriveType = "google-drive";

    // The link's text by source type; any other type reads "Open at the source".
    private static readonly Dictionary<string, string> s_linkText = new(StringComparer.Ordinal)
    {
        ["google-takeout"] = "Source_OpenGooglePhotos",
        ["instagram"] = "Source_OpenInstagram",
    };

    // The line for an original that stays at an export's source and has no address, by export kind; any other export kind
    // reads "Stored in {name}".
    private static readonly Dictionary<string, string> s_storedLine = new(StringComparer.Ordinal)
    {
        ["apple-photos"] = "Source_StoredApple",
        ["facebook"] = "Source_StoredFacebook",
    };

    // Cloud sources (D-023) are in reference mode; S-038 and S-039 add their "Open original" and their line to this list.
    private static readonly HashSet<string> s_cloudTypes = new(StringComparer.Ordinal) { "google-drive", "onedrive" };

    /// <summary>
    /// The address to link to, or null: only an absolute <c>https://</c> address without a user name or password is ever a link (a hand-edited catalog row
    /// could hold <c>javascript:</c> or anything else), and it is never followed by the server.
    /// </summary>
    // No test for an empty host is needed: Uri.TryCreate refuses an https address without one ("https://", "https:///x",
    // both pinned in the tests).
    public static string? SafeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.UserInfo.Length == 0
            ? uri.AbsoluteUri
            : null;

    /// <summary>
    /// The address of "Open original" through the server (S-038), for a Google Drive source whose file is still there: the server reads the
    /// file from the Drive and passes it on. Null for any other source, for a file the sync found gone, and when the original is stored (the
    /// page's own download button serves it).
    /// </summary>
    public static string? OriginalHref(string assetId, AssetSourceDetail source, bool originalStored)
    {
        ArgumentNullException.ThrowIfNull(source);
        return !originalStored && source is { Type: GoogleDriveType, Id: > 0, Removed: false }
            ? $"photos/{assetId}/sources/{source.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}/original"
            : null;
    }

    /// <summary>"Open in Google Photos", "Open on Instagram" or "Open at the source".</summary>
    public static string LinkText(AssetSourceDetail source, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(text);
        return text[s_linkText.GetValueOrDefault(source.Type, "Source_OpenAtSource")].Value;
    }

    /// <summary>
    /// Where an original that Sepiabox does not keep is, for a source with no link: a sentence about the drive or the
    /// export it stayed in, and the drive path to show after it in its own markup (null when the sentence is whole).
    /// Null for a cloud source, whose line the cloud stories add.
    /// </summary>
    public static SourceLine? StoredLine(AssetSourceDetail source, IStringLocalizer<SharedResource> text)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(text);
        if (source.Type == LocalType)
        {
            return new SourceLine(text["Source_StoredOnDrive", source.Name].Value, source.Path);
        }

        return s_cloudTypes.Contains(source.Type)
            ? null
            : new SourceLine(s_storedLine.TryGetValue(source.Type, out var key) ? text[key].Value : text["Source_StoredIn", source.Name].Value, null);
    }
}

/// <summary>A sentence about where an original is, and the path that follows it when it has one.</summary>
public sealed record SourceLine(string Text, string? Path);
