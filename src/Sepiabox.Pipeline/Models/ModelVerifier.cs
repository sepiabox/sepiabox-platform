using System.Globalization;
using System.Security.Cryptography;

namespace Sepiabox.Pipeline.Models;

/// <summary>
/// Checks a downloaded model against its pinned SHA-256 (D-016). Hashing 350 MB on every run would cost
/// seconds, so a successful check is remembered in a marker next to the model, valid while the file keeps
/// the same size and modification time.
/// </summary>
public static class ModelVerifier
{
    public static string MarkerPath(string modelPath) => modelPath + ".verified";

    /// <summary>True when the file at <paramref name="path"/> has the pinned SHA-256; otherwise its actual SHA-256.</summary>
    public static bool Verify(ModelInfo model, string path, out string actualSha256)
    {
        var file = new FileInfo(path);
        var expectedMarker = Marker(model, file);
        if (ReadMarker(path) == expectedMarker)
        {
            actualSha256 = model.Sha256;
            return true;
        }

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
        {
            actualSha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        if (!string.Equals(actualSha256, model.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(MarkerPath(path));
            return false;
        }

        Remember(model, path);
        return true;
    }

    /// <summary>Records that the file at <paramref name="path"/> was just verified against <paramref name="model"/>.</summary>
    public static void Remember(ModelInfo model, string path)
    {
        try
        {
            File.WriteAllText(MarkerPath(path), Marker(model, new FileInfo(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only models folder only means the file is hashed again on the next run.
        }
    }

    private static string Marker(ModelInfo model, FileInfo file) =>
        string.Create(CultureInfo.InvariantCulture, $"{model.Sha256.ToLowerInvariant()} {file.Length} {file.LastWriteTimeUtc.Ticks}");

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

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A marker left behind is trusted only while the size and modification time match, which a rewritten file rarely keeps.
        }
    }
}
