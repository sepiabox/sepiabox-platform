using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class MpegSignatureTests
{
    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0xBA }, 0)]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0xB3 }, 0)]
    [InlineData(new byte[] { 0x47 }, 188)]
    [InlineData(new byte[] { 0, 0, 0, 0, 0x47 }, 196)]
    public void Recognises_mpeg_when_the_file_starts_with_a_start_code_or_packets(byte[] start, int secondSync)
    {
        using var stream = new MemoryStream(Bytes(start, secondSync));

        Assert.True(MpegSignature.IsMatch(stream));
    }

    [Theory]
    // A module title starting with "G", the sync byte's value, with no second sync byte 188 bytes later.
    [InlineData(new byte[] { 0x47, 0x61 }, 0)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0)]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 }, 0)]
    public void Does_not_recognise_mpeg_when_the_start_is_anything_else(byte[] start, int secondSync)
    {
        using var stream = new MemoryStream(Bytes(start, secondSync));

        Assert.False(MpegSignature.IsMatch(stream));
    }

    [Fact]
    public void Does_not_recognise_mpeg_when_the_file_is_shorter_than_a_start_code()
    {
        using var stream = new MemoryStream([0x00, 0x00]);

        Assert.False(MpegSignature.IsMatch(stream));
    }

    [Fact]
    public void Puts_the_stream_position_back_and_refuses_streams_that_cannot_seek()
    {
        using var stream = new MemoryStream(Bytes([0x00, 0x00, 0x01, 0xBA], 0));
        stream.Position = 7;

        Assert.True(MpegSignature.IsMatch(stream));
        Assert.Equal(7, stream.Position);
        using var forwardOnly = new ForwardOnlyStream(Bytes([0x00, 0x00, 0x01, 0xBA], 0));
        Assert.False(MpegSignature.IsMatch(forwardOnly));
    }

    [Fact]
    public void Says_no_when_the_file_cannot_be_read()
    {
        using var temp = new TempFolder();

        Assert.False(MpegSignature.IsMatch(Path.Combine(temp.Path, "missing.mts")));
    }

    private static byte[] Bytes(byte[] start, int secondSync)
    {
        var bytes = new byte[1024];
        start.CopyTo(bytes, 0);
        if (secondSync > 0)
        {
            bytes[secondSync] = 0x47;
        }

        return bytes;
    }
}

/// <summary>A stream that, like a network or pipe stream, can only be read forwards.</summary>
internal sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes)
{
    public override bool CanSeek => false;
}
