using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class MpegSignatureTests
{
    [Theory]
    [InlineData(0xBA)] // program stream pack header
    [InlineData(0xBB)] // system header
    [InlineData(0xB3)] // video sequence header
    [InlineData(0xC0)] // first audio PES stream
    [InlineData(0xE0)] // first video PES stream
    [InlineData(0xEF)] // last video PES stream
    public void Recognises_mpeg_when_the_file_starts_with_a_system_or_video_start_code(int streamId)
    {
        using var stream = new MemoryStream(Bytes([0x00, 0x00, 0x01, (byte)streamId]));

        Assert.True(MpegSignature.IsMatch(stream));
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00 })]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0xB5 })]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0xF0 })]
    // An MP4 file whose first box is 256 bytes long.
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00, (byte)'f', (byte)'t', (byte)'y', (byte)'p' })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x01 })]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 })]
    public void Does_not_recognise_mpeg_when_the_start_is_anything_else(byte[] start)
    {
        using var stream = new MemoryStream(Bytes(start));

        Assert.False(MpegSignature.IsMatch(stream));
    }

    [Theory]
    [InlineData(0, 188)]
    [InlineData(4, 192)]
    public void Recognises_transport_streams_when_three_packets_start_with_the_sync_byte(int first, int size)
    {
        using var stream = new MemoryStream(Bytes([], syncs: [first, first + size, first + (2 * size)]));

        Assert.True(MpegSignature.IsMatch(stream));
    }

    [Theory]
    [InlineData(0, 188)]
    [InlineData(4, 192)]
    public void Does_not_recognise_transport_streams_when_a_long_file_has_only_two_sync_bytes(int first, int size)
    {
        using var stream = new MemoryStream(Bytes([], syncs: [first, first + size]));

        Assert.False(MpegSignature.IsMatch(stream));
    }

    [Theory]
    [InlineData(0, 188, 300)]
    [InlineData(4, 192, 300)]
    public void Accepts_two_sync_bytes_when_the_file_is_too_short_for_a_third(int first, int size, int length)
    {
        using var stream = new MemoryStream(Bytes([], syncs: [first, first + size], length));

        Assert.True(MpegSignature.IsMatch(stream));
    }

    [Fact]
    public void Does_not_recognise_mpeg_when_the_file_is_shorter_than_a_start_code()
    {
        using var stream = new MemoryStream([0x00, 0x00, 0x01]);

        Assert.False(MpegSignature.IsMatch(stream));
    }

    [Fact]
    public void Puts_the_stream_position_back_and_refuses_streams_that_cannot_seek()
    {
        using var stream = new MemoryStream(Bytes([0x00, 0x00, 0x01, 0xBA]));
        stream.Position = 7;

        Assert.True(MpegSignature.IsMatch(stream));
        Assert.Equal(7, stream.Position);
        using var forwardOnly = new ForwardOnlyStream(Bytes([0x00, 0x00, 0x01, 0xBA]));
        Assert.False(MpegSignature.IsMatch(forwardOnly));
    }

    [Fact]
    public void Throws_rather_than_answering_when_the_file_cannot_be_read()
    {
        using var temp = new TempFolder();

        Assert.ThrowsAny<IOException>(() => MpegSignature.IsMatch(Path.Combine(temp.Path, "missing.mts")));
    }

    private static byte[] Bytes(byte[] start, int[]? syncs = null, int length = 1024)
    {
        var bytes = new byte[length];
        start.CopyTo(bytes, 0);
        foreach (var offset in syncs ?? [])
        {
            bytes[offset] = 0x47;
        }

        return bytes;
    }
}

/// <summary>A stream that, like a network or pipe stream, can only be read forwards.</summary>
internal sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes)
{
    public override bool CanSeek => false;
}
