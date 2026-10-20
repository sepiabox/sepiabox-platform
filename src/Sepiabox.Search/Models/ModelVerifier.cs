using System.Globalization;
using System.Security.Cryptography;

namespace Sepiabox.Search.Models;

/// <summary>
/// Checks a downloaded model against its pinned SHA-256 (D-016, D-044). Hashing 350 MB on every run would cost
/// seconds, so a successful check is remembered in a marker next to the model, trusted while the file keeps the
/// same size and modification time. <c>models download</c> never trusts the marker, so it can repair a file
/// that was corrupted in place.
/// </summary>
public static class ModelVerifier
{
    public static string MarkerPath(string modelPath) => modelPath + ".verified";

    /// <summary>
    /// True when the file at <paramref name="path"/> has the pinned SHA-256, with its actual SHA-256 otherwise.
    /// <paramref name="trustMarker"/> false hashes the file even when a marker says it was verified;
    /// <paramref name="fromMarker"/> says the answer came from the marker, without hashing.
    /// </summary>
    public static bool Verify(ModelInfo model, string path, bool trustMarker, out string actualSha256, out bool fromMarker)
    {
        // Taken before hashing: a file that changes while it is read must not be remembered as verified.
        var before = new FileInfo(path);
        var marker = Marker(model, before.Length, before.LastWriteTimeUtc);
        if (trustMarker && ReadMarker(path) == marker)
        {
            actualSha256 = model.Sha256;
            fromMarker = true;
            return true;
        }

        fromMarker = false;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
        {
            actualSha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        if (!string.Equals(actualSha256, model.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Forget(path);
            return false;
        }

        var after = new FileInfo(path);
        if (after.Length == before.Length && after.LastWriteTimeUtc == before.LastWriteTimeUtc)
        {
            WriteMarker(path, marker);
        }

        return true;
    }

    /// <summary>Records that the file at <paramref name="path"/> was just verified against <paramref name="model"/>.</summary>
    public static void Remember(ModelInfo model, string path)
    {
        var file = new FileInfo(path);
        WriteMarker(path, Marker(model, file.Length, file.LastWriteTimeUtc));
    }

    /// <summary>Removes the marker, so the next check hashes the file again.</summary>
    public static void Forget(string path)
    {
        try
        {
            File.Delete(MarkerPath(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A marker left behind is trusted only while the size and modification time match, which a rewritten file rarely keeps.
        }
    }

    private static string Marker(ModelInfo model, long length, DateTime lastWriteUtc) =>
        string.Create(CultureInfo.InvariantCulture, $"{model.Sha256.ToLowerInvariant()} {length} {lastWriteUtc.Ticks}");

    private static void WriteMarker(string path, string marker)
    {
        try
        {
            File.WriteAllText(MarkerPath(path), marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only models folder only means the file is hashed again on the next run.
        }
    }

    private static string? ReadMarker(string path)
    {
        try
        {
            return File.ReadAllText(MarkerPath(path)).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
