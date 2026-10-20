using System.Buffers.Binary;
using System.Text;

namespace Sepiabox.Tests.Shared;

/// <summary>
/// Writes the smallest QuickTime movie MetadataExtractor reads, atom by atom, with the ISO 6709 position the way
/// phones store it. It has no tracks, so FFmpeg cannot decode it: tests that run FFmpeg generate their videos with it.
/// </summary>
internal static class TestQuickTime
{
    /// <summary>
    /// A two-second movie with <paramref name="userData"/> in the <c>moov/udta/©xyz</c> atom (Android, older
    /// iPhones) and <paramref name="appleKey"/> under the <c>com.apple.quicktime.location.ISO6709</c> key of
    /// <c>moov/meta</c> (iPhones), each only when given.
    /// <paramref name="keySize"/> overrides the size the <c>keys</c> atom declares for its entry, to make it malformed.
    /// </summary>
    public static byte[] Movie(string? userData = null, string? appleKey = null, uint? keySize = null)
    {
        // Version, flags, creation and modification time, then the time scale and duration that make two seconds.
        var header = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), 1000);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), 2000);
        List<byte[]> movie = [Atom("mvhd", header)];

        if (userData is not null)
        {
            // A 16-bit length and a packed language code before the text; 0x15C7 is "eng".
            var text = Encoding.ASCII.GetBytes(userData);
            movie.Add(Atom("udta", Atom("©xyz", UInt16((ushort)text.Length), UInt16(0x15C7), text)));
        }

        if (appleKey is not null)
        {
            var key = Encoding.ASCII.GetBytes("com.apple.quicktime.location.ISO6709");
            movie.Add(Atom(
                "meta",
                Atom("hdlr", new byte[8], Encoding.ASCII.GetBytes("mdta"), new byte[13]),
                Atom("keys", new byte[4], UInt32(1), UInt32(keySize ?? (uint)(8 + key.Length)), Encoding.ASCII.GetBytes("mdta"), key),
                // Each ilst entry is named by its one-based index into keys; data type 1 is UTF-8 text.
                Atom("ilst", Atom(UInt32(1), Atom("data", UInt32(1), new byte[4], Encoding.ASCII.GetBytes(appleKey))))));
        }

        return [.. Atom("ftyp", Encoding.ASCII.GetBytes("qt  "), new byte[4], Encoding.ASCII.GetBytes("qt  ")), .. Atom("moov", [.. movie])];
    }

    private static byte[] Atom(string type, params byte[][] content) => Atom(Encoding.Latin1.GetBytes(type), content);

    private static byte[] Atom(byte[] type, params byte[][] content)
    {
        var body = content.SelectMany(part => part).ToArray();
        return [.. UInt32((uint)(8 + body.Length)), .. type, .. body];
    }

    private static byte[] UInt32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] UInt16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }
}
