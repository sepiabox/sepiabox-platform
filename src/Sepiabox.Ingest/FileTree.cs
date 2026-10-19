using System.IO.Enumeration;

namespace Sepiabox.Ingest;

public readonly record struct FoundFile(string Path, long Size, DateTimeOffset ModifiedAt);

/// <summary>Walks a folder tree the way every command sees it, skipping system and trash folders.</summary>
public static class FileTree
{
    // Operating system and NAS folders that never hold the user's own photos.
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN", "System Volume Information", ".Trashes", ".Spotlight-V100", ".fseventsd",
        ".DocumentRevisions-V100", ".TemporaryItems", "@eaDir", ".thumbnails",
    };

    /// <summary>Full path of an existing folder given on the command line.</summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public static string ResolveRoot(string folder)
    {
        // On Windows "E:" means the current folder on drive E, not the drive itself.
        var requested = folder is [_, ':'] ? folder + Path.DirectorySeparatorChar : folder;
        var root = Path.GetFullPath(requested);
        return Directory.Exists(root) ? root : throw new DirectoryNotFoundException($"Folder not found: {root}");
    }

    public static FileSystemEnumerable<FoundFile> Enumerate(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        return new FileSystemEnumerable<FoundFile>(
            root,
            (ref FileSystemEntry entry) => new FoundFile(entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !IgnoredFolders.Contains(entry.FileName.ToString()),
        };
    }
}
