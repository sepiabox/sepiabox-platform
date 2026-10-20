namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Whether a file begins like MPEG: an MPEG-1/2 start code (<c>00 00 01 xx</c>, such as a program stream's pack
/// header), or transport stream sync bytes (<c>0x47</c>) every 188 bytes, or every 192 for the time-stamped
/// packets AVCHD writes to <c>.mts</c>. A camcorder file that looks like this is never left out as "not a video":
/// when FFmpeg finds no video in it, it is reported as damaged instead.
/// </summary>
public static class MpegSignature
{
    private const byte SyncByte = 0x47;

    /// <summary>False for a file that cannot be read; whatever reads it next reports why.</summary>
    public static bool IsMatch(string path)
    {
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
        try
        {
            return StartCode(stream)
                || (ByteAt(stream, 0) == SyncByte && ByteAt(stream, 188) == SyncByte)
                || (ByteAt(stream, 4) == SyncByte && ByteAt(stream, 196) == SyncByte);
        }
        finally
        {
            stream.Position = position;
        }
    }

    private static bool StartCode(Stream stream) => ByteAt(stream, 0) == 0x00 && ByteAt(stream, 1) == 0x00 && ByteAt(stream, 2) == 0x01;

    // -1 past the end, which matches no byte.
    private static int ByteAt(Stream stream, int offset)
    {
        if (offset >= stream.Length)
        {
            return -1;
        }

        stream.Position = offset;
        return stream.ReadByte();
    }
}
