using System.Text;

namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Tells a tracker-music module (Amiga ProTracker and its descendants) from a camcorder's MPEG-2 file, since both
/// use <c>.mod</c>. Reads the format tag ProTracker-family modules carry at offset 1080, so the answer does not
/// depend on whether the user's FFmpeg was built with a module decoder. A file that is left out is never
/// catalogued, so anything that looks like MPEG is never called a module.
/// </summary>
public static class TrackerModule
{
    /// <summary>Why a file is left out when this says it is a module, for the run's list of left-out files.</summary>
    public const string Reason = "Tracker-music module (ProTracker format tag at offset 1080), not a video.";

    private const int TagOffset = 1080;
    private const int TransportPacket = 188;

    private static readonly HashSet<string> Tags = new(StringComparer.Ordinal)
    {
        "M.K.", "M!K!", "M&K!", "4CHN", "6CHN", "8CHN", "FLT4", "FLT8", "CD81", "OKTA",
    };

    /// <summary>
    /// True for a <c>.mod</c> file that is a tracker module. Any other extension, or a file that cannot be read,
    /// is false: the probe that follows reports a file it cannot read.
    /// </summary>
    public static bool IsTrackerModule(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".mod", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            // Only a few bytes are needed, so no read-ahead buffer; the user's file is opened for reading only.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1);
            return IsMatch(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when <paramref name="stream"/>, read from its start, holds a ProTracker-family module.</summary>
    public static bool IsMatch(Stream stream)
    {
        if (stream.Length < TagOffset + 4)
        {
            return false;
        }

        Span<byte> start = stackalloc byte[4];
        stream.Position = 0;
        stream.ReadExactly(start);
        // An MPEG program stream begins with a pack header, a transport stream with a sync byte every 188 bytes.
        if (start.SequenceEqual((ReadOnlySpan<byte>)[0x00, 0x00, 0x01, 0xBA]) || (start[0] == 0x47 && ByteAt(stream, TransportPacket) == 0x47))
        {
            return false;
        }

        Span<byte> tag = stackalloc byte[4];
        stream.Position = TagOffset;
        stream.ReadExactly(tag);
        return IsTag(Encoding.ASCII.GetString(tag));
    }

    private static int ByteAt(Stream stream, int offset)
    {
        stream.Position = offset;
        return stream.ReadByte();
    }

    // Besides the fixed tags, "nCHN" for 1-9 channels and "nnCH" or "nnCN" for 10-99 (FastTracker, TakeTracker).
    private static bool IsTag(string tag) =>
        Tags.Contains(tag)
        || (IsLeadingDigit(tag[0]) && tag[1..] == "CHN")
        || (IsLeadingDigit(tag[0]) && IsDigit(tag[1]) && tag[2..] is "CH" or "CN");

    private static bool IsLeadingDigit(char c) => c is >= '1' and <= '9';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
