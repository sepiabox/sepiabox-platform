using System.Text.Json;
using System.Text.RegularExpressions;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Exports;

/// <summary>
/// Reads an extracted Instagram "Download your information" (JSON) (S-048, D-053, D-111): <c>process --export instagram</c>.
/// The post lists (<c>posts_*.json</c>, <c>archived_posts.json</c>, <c>stories.json</c>, <c>reels.json</c> under
/// <c>your_instagram_activity/content/</c>, or <c>content/</c> in older downloads) say when each media file was posted
/// (UTC epoch seconds), what the caption was, and sometimes where and when it was taken (<c>exif_data</c>); Instagram strips
/// the files' own metadata. Media files that no list names (profile pictures, messages) are left out and counted. The
/// download is only read.
/// </summary>
public sealed partial class InstagramReader : IExportReader
{
    public const string ExportKind = "instagram";

    public const string PostsAlbum = "Instagram";
    public const string StoriesAlbum = "Instagram stories";
    public const string ReelsAlbum = "Instagram reels";

    // The folder holding the lists; the download's root (where media/ is) is the folder above it, or above
    // your_instagram_activity/ when the download has it (stripped by Activity()).
    [GeneratedRegex(@"^(?<root>.*?/)?content/(?<list>posts_\d+|archived_posts|stories|reels)\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex PostList();

    // The lists are in your_instagram_activity/content/ and the media in media/ beside that folder, not inside it.
    [GeneratedRegex(@"(?<=^|/)your_instagram_activity/$", RegexOptions.IgnoreCase)]
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
        var lists = all.Select(f => (Path: f, Match: PostList().Match(f))).Where(x => x.Match.Success).ToList();
        if (lists.Count == 0)
        {
            throw new ExportReadException(
                "No Instagram post list was found in the folder. Expected your_instagram_activity/content/posts_1.json (older downloads: content/posts_1.json) in a download requested in JSON format.");
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
                var album = Albums(match.Groups["list"].Value);
                foreach (var media in MetaExport.Items(document.RootElement, () => tooDeep++))
                {
                    var relative = MetaExport.Resolve(root, baseFolder, media);
                    if (relative is null)
                    {
                        missing++;
                        continue;
                    }

                    var facts = new MetaFacts(media.Title, media.PostedAt, media.Exif, album);
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
            warnings.Add(
                $"{unlisted.Count:N0} photos and videos in the download are in no post list and were not imported: profile pictures, messages, other people's media, and also your own highlights, IGTV and archived stories if the download keeps them in lists this reader does not read. Check this number against your account.");
        }

        if (missing > 0)
        {
            warnings.Add($"{missing:N0} media files named in the post lists are not in the download.");
        }

        if (tooDeep > 0)
        {
            warnings.Add($"{tooDeep:N0} entries in the post lists are nested deeper than {MetaExport.MaxDepth} levels and were not read.");
        }

        if (unreadable.Count > 0)
        {
            warnings.Add($"{unreadable.Count:N0} post lists could not be read and were ignored: {string.Join(", ", unreadable.Take(5))}.");
        }

        return new ExportScan(files, unlisted, warnings);
    }

    private static string[] Albums(string list) =>
        list.ToLowerInvariant() switch
        {
            "stories" => [PostsAlbum, StoriesAlbum],
            "reels" => [PostsAlbum, ReelsAlbum],
            _ => [PostsAlbum],
        };
}
