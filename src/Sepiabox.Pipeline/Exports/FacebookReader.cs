using System.Text.Json;
using System.Text.RegularExpressions;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Exports;

/// <summary>
/// Reads an extracted Facebook "Download your information" (JSON) (S-049, D-053, D-112): <c>process --export facebook</c>.
/// The album files (<c>posts/album/&lt;n&gt;.json</c>, under <c>your_facebook_activity/</c> or not) name an album and list its
/// photos; the posts file (<c>posts/your_posts__check_ins__photos_and_videos_&lt;n&gt;.json</c>) lists posts with their text
/// and attachments. Each media file gets its caption (its own <c>description</c>, else the post's text), its date (when it
/// was taken, else when it was posted) and its places and albums from them. Facebook strips the files' own metadata. Media
/// that no list names (Messenger, profile pictures) are left out and counted. The download is only read.
/// </summary>
public sealed partial class FacebookReader : IExportReader
{
    public const string ExportKind = "facebook";

    public const string FacebookAlbum = "Facebook";

    // The lists are in posts/, under your_facebook_activity/ or not (older downloads); media/ paths are relative to the folder above.
    [GeneratedRegex(@"^(?<root>.*?/)?posts/(?<list>album/\d+|your_posts__check_ins__photos_and_videos_\d+)\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex List();

    [GeneratedRegex(@"(?<=^|/)your_facebook_activity/$", RegexOptions.IgnoreCase)]
    private static partial Regex Activity();

    public string Kind => ExportKind;

    public ExportScan Read(string root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!Directory.Exists(root))
        {
            throw new ExportReadException($"The export folder {root} does not exist.");
        }

        var all = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        var lists = all.Select(f => (Path: f, Match: List().Match(f))).Where(x => x.Match.Success).ToList();
        if (lists.Count == 0)
        {
            throw new ExportReadException(
                "No Facebook album or post list was found in the folder. Expected your_facebook_activity/posts/album/0.json (older downloads: posts/album/0.json) in a download requested in JSON format.");
        }

        var described = new Dictionary<string, MetaFacts>(StringComparer.OrdinalIgnoreCase);
        var unreadable = new List<string>();
        var missing = 0;
        var tooDeep = 0;
        foreach (var (path, match) in lists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, path)));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                unreadable.Add(path);
                continue;
            }

            using (document)
            {
                var baseFolder = Activity().Replace(match.Groups["root"].Value, string.Empty);
                foreach (var (media, albums) in match.Groups["list"].Value.StartsWith("album/", StringComparison.OrdinalIgnoreCase)
                    ? AlbumMedia(document.RootElement, () => tooDeep++)
                    : PostMedia(document.RootElement, () => tooDeep++))
                {
                    var relative = MetaExport.Resolve(root, baseFolder, media);
                    if (relative is null)
                    {
                        missing++;
                        continue;
                    }

                    var facts = new MetaFacts(media.Text, media.PostedAt, media.Exif, albums);
                    described[relative] = described.TryGetValue(relative, out var earlier) ? earlier.Merge(facts) : facts;
                }
            }
        }

        var files = described.Select(pair => new KeyValuePair<string, ExportMetadata>(pair.Key, pair.Value.ToMetadata())).ToList();
        var listed = new HashSet<string>(described.Keys, StringComparer.OrdinalIgnoreCase);
        var unlisted = all.Where(f => MediaTypes.Classify(f) != MediaKind.Other && !listed.Contains(f)).ToList();

        var warnings = new List<string>();
        if (unlisted.Count > 0)
        {
            warnings.Add($"{unlisted.Count:N0} photos and videos in the download are in no album or post list (Messenger, profile pictures, other people's media, and possibly lists this reader does not read) and were not imported. Check this number against your account.");
        }

        if (missing > 0)
        {
            warnings.Add($"{missing:N0} media files named in the lists are not in the download.");
        }

        if (tooDeep > 0)
        {
            warnings.Add($"{tooDeep:N0} entries in the lists are nested deeper than {MetaExport.MaxDepth} levels and were not read.");
        }

        if (unreadable.Count > 0)
        {
            warnings.Add($"{unreadable.Count:N0} lists could not be read and were ignored: {string.Join(", ", unreadable.Take(5))}.");
        }

        return new ExportScan(files, unlisted, warnings);
    }

    // An album file: its name, and the photos (only: the album's own description and cover are not a photo's caption).
    private static IEnumerable<(MetaMedia Media, string[] Albums)> AlbumMedia(JsonElement root, Action tooDeep)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? MetaExport.RepairText(n.GetString()!).Trim() : string.Empty;
        string[] albums = name.Length == 0 ? [FacebookAlbum] : [name, FacebookAlbum];
        foreach (var key in new[] { "photos", "videos" })
        {
            if (!root.TryGetProperty(key, out var items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var media in MetaExport.Items(items, tooDeep))
            {
                yield return (media, albums);
            }
        }
    }

    // The posts file: a list of posts, each with the text in data[].post, a time in timestamp and its media in attachments[].
    private static IEnumerable<(MetaMedia Media, string[] Albums)> PostMedia(JsonElement root, Action tooDeep)
    {
        var posts = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToList(),
            JsonValueKind.Object => [.. root.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array).SelectMany(p => p.Value.EnumerateArray())],
            _ => [],
        };
        foreach (var post in posts.Where(p => p.ValueKind == JsonValueKind.Object))
        {
            var text = PostText(post);
            var postedAt = post.TryGetProperty("timestamp", out var t) ? MetaExport.Epoch(t) : null;
            if (!post.TryGetProperty("attachments", out var attachments))
            {
                continue;
            }

            foreach (var media in MetaExport.Items(attachments, text, postedAt, tooDeep))
            {
                yield return (media, [FacebookAlbum]);
            }
        }
    }

    private static string? PostText(JsonElement post)
    {
        if (!post.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return data.EnumerateArray()
            .Where(d => d.ValueKind == JsonValueKind.Object && d.TryGetProperty("post", out var p) && p.ValueKind == JsonValueKind.String)
            .Select(d => MetaExport.RepairText(d.GetProperty("post").GetString()!).Trim())
            .FirstOrDefault(text => text.Length > 0);
    }
}
