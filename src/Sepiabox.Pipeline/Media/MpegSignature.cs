namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Whether a file begins like MPEG: an MPEG system or video start code (a program stream's pack or system header,
/// a sequence header, or a PES packet), or transport stream sync bytes (<c>0x47</c>) every 188 bytes, or every 192
/// for the time-stamped packets AVCHD writes to <c>.mts</c>. A camcorder file that looks like this is never left
/// out as "not a video": when FFmpeg finds no video in it, it is reported as damaged instead.
/// </summary>
public static class MpegSignature
{
    private const byte SyncByte = 0x47;

    // Enough for three 192-byte packets after AVCHD's 4-byte time stamp.
    private const int HeadLength = 400;

    /// <summary>
    /// Opens <paramref name="path"/> for reading only. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when it cannot be read: "cannot tell" is not "not MPEG".
    /// </summary>
    public static bool IsMatch(string path)
    {
        // Only a few bytes are needed, so no read-ahead buffer.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1);
        return IsMatch(stream);
    }

    /// <summary>
    /// Reads <paramref name="stream"/> from its start and puts its position back. A stream that cannot seek is
    /// never a match.
    /// </summary>
    public static bool IsMatch(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        var position = stream.Position;
        Span<byte> head = stackalloc byte[HeadLength];
        int length;
        try
        {
            stream.Position = 0;
            length = stream.ReadAtLeast(head, HeadLength, throwOnEndOfStream: false);
        }
        finally
        {
            stream.Position = position;
        }

        head = head[..length];
        return IsStartCode(head) || HasPackets(head, first: 0, size: 188) || HasPackets(head, first: 4, size: 192);
    }

    // 00 00 01 then a system or video stream id; other ids, and other files that happen to start with 00 00 01
    // (an MP4 box 256 to 511 bytes long), do not count.
    private static bool IsStartCode(ReadOnlySpan<byte> head) =>
        head.Length >= 4 && head[0] == 0x00 && head[1] == 0x00 && head[2] == 0x01
        && head[3] is 0xBA or 0xBB or 0xB3 or (>= 0xC0 and <= 0xEF);

    // Three sync bytes a packet apart, or two when the file is too short for a third.
    private static bool HasPackets(ReadOnlySpan<byte> head, int first, int size)
    {
        var needed = head.Length > first + (2 * size) ? 3 : 2;
        if (head.Length <= first + ((needed - 1) * size))
        {
            return false;
        }

        for (var packet = 0; packet < needed; packet++)
        {
            if (head[first + (packet * size)] != SyncByte)
            {
                return false;
            }
        }

        return true;
    }
}
