using System.Security.Cryptography;

namespace Sepiabox.Pipeline.Media;

public static class MediaFile
{
    // Read-only and shared: the source drive is never modified.
    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 1024, FileOptions.SequentialScan);

    /// <summary>SHA-256 of the whole file as lowercase hex: the identity of an asset (D-014).</summary>
    public static string Sha256Hex(string path)
    {
        using var stream = OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
