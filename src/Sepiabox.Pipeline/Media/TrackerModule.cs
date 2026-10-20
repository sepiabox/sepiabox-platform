using System.Text;

namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Tells a tracker-music module (Amiga ProTracker and its descendants) from a camcorder's MPEG-2 file, since both
/// use <c>.mod</c>. Reads the format tag ProTracker-family modules carry at offset 1080, so the answer does not
/// depend on whether the user's FFmpeg was built with a module decoder.
/// </summary>
public static class TrackerModule
{
    private const int TagOffset = 1080;

    private static readonly HashSet<string> Tags = new(StringComparer.Ordinal)
    {
        "M.K.", "M!K!", "M&K!", "4CHN", "6CHN", "8CHN", "FLT4", "FLT8", "CD81", "OKTA",
    };

    public static bool IsMatch(string path)
    {
        Span<byte> tag = stackalloc byte[4];
        // Only four bytes are needed, so no read-ahead buffer; the user's file is opened for reading only.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1);
        if (stream.Length < TagOffset + tag.Length)
        {
            return false;
        }

        stream.Position = TagOffset;
        stream.ReadExactly(tag);
        return IsTag(Encoding.ASCII.GetString(tag));
    }

    // Besides the fixed tags, "nCHN" for 1-9 channels and "nnCH" or "nnCN" for 10-99 (FastTracker, TakeTracker).
    private static bool IsTag(string tag) =>
        Tags.Contains(tag)
        || (IsDigit(tag[0]) && tag[1..] == "CHN")
        || (IsDigit(tag[0]) && IsDigit(tag[1]) && tag[2..] is "CH" or "CN");

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
