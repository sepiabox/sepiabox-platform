using ImageMagick;
using Sepiabox.Pipeline.Imaging;

namespace Sepiabox.Pipeline.Tests;

public class ThumbnailGeneratorTests
{
    private const string Folder = "derivatives/abc";

    [Fact]
    public void Fits_each_size_to_its_longest_side_keeping_the_aspect_ratio()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("photo.jpg", TestPhotos.Sized(4000, 3000));

        var derivatives = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes);

        Assert.Equal(
            [("thumb-256", 256, 192), ("preview-1280", 1280, 960)],
            derivatives.Select(d => (d.Name, d.Width, d.Height)));
        foreach (var derivative in derivatives)
        {
            Assert.Equal($"{Folder}/{derivative.Name}.webp", derivative.Path);
            Assert.Equal("image/webp", derivative.ContentType);
            using var written = new MagickImage(Path.Combine(temp.Path, derivative.Path));
            Assert.Equal(MagickFormat.WebP, written.Format);
            Assert.Equal(((uint)derivative.Width, (uint)derivative.Height), (written.Width, written.Height));
        }
    }

    [Fact]
    public void Applies_the_exif_orientation_to_the_pixels()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("sideways.jpg", TestPhotos.Sized(400, 300, orientation: 6));

        var thumb = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes)[0];

        Assert.True(thumb.Height > thumb.Width, $"Expected portrait, got {thumb.Width}×{thumb.Height}.");
        Assert.Equal((192, 256), (thumb.Width, thumb.Height));
    }

    [Fact]
    public void Removes_all_metadata_including_gps()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("porto.jpg", TestPhotos.Porto());

        var thumb = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes)[0];

        using var written = new MagickImage(Path.Combine(temp.Path, thumb.Path));
        Assert.Null(written.GetExifProfile());
        Assert.Null(written.GetXmpProfile());
    }

    [Fact]
    public void Does_not_upscale_a_photo_smaller_than_the_target()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("small.jpg", TestPhotos.Sized(200, 100));

        var derivatives = ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes);

        Assert.All(derivatives, d => Assert.Equal((200, 100), (d.Width, d.Height)));
    }

    [Fact]
    public void Throws_a_magick_exception_for_a_corrupted_image()
    {
        using var temp = new TempFolder();
        var photo = temp.Write("broken.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46]);

        Assert.ThrowsAny<MagickException>(() => ThumbnailGenerator.Generate(photo, temp.Path, Folder, ThumbnailGenerator.PhotoSizes));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, Folder)) && Directory.EnumerateFiles(Path.Combine(temp.Path, Folder)).Any());
    }

    [Fact]
    public void Uses_the_full_size_camera_jpeg_when_the_photo_is_a_canon_raw()
    {
        using var temp = new TempFolder();
        var raw = temp.Write("IMG_0001.CR2", CanonRaw.Build(1536, 1024, orientation: 6));

        var derivatives = ThumbnailGenerator.Generate(raw, temp.Path, Folder, ThumbnailGenerator.PhotoSizes);

        // From the 1536x1024 JPEG in IFD0, turned upright; not the 160x120 thumbnail in IFD1 nor the small RGB image in IFD2.
        Assert.Equal([("thumb-256", 171, 256), ("preview-1280", 853, 1280)], derivatives.Select(d => (d.Name, d.Width, d.Height)));
        using var preview = new MagickImage(Path.Combine(temp.Path, derivatives[1].Path));
        var pixel = preview.GetPixels().GetPixel(426, 640).ToColor()!;
        Assert.True(pixel.R > 230 && pixel.G is > 140 and < 190 && pixel.B < 40, $"Expected the orange of IFD0, got {pixel}.");
    }
}

/// <summary>
/// A file laid out like a Canon CR2 (TIFF-based, version 2): IFD0 holds the camera's full-size JPEG as strips
/// with old-style JPEG compression, BitsPerSample 8,8,8, Make, Model and an Exif IFD; IFD1 a 160x120 JPEG
/// thumbnail referenced by JPEGInterchangeFormat; IFD2 a small uncompressed RGB image; IFD3 the sensor data
/// (lossless JPEG in a real file, filler here), whose offset is also in the CR2 header. Each image has its own
/// colour so a test can tell which one was decoded.
/// </summary>
internal static class CanonRaw
{
    private const ushort Short = 3;
    private const ushort Long = 4;
    private const ushort Ascii = 2;
    private const ushort Rational = 5;

    public static byte[] Build(int width, int height, int orientation)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("II*\0"u8);
        writer.Write(0u); // IFD0, patched below
        writer.Write("CR"u8);
        writer.Write((byte)2);
        writer.Write((byte)0);
        writer.Write(0u); // IFD3, the raw data, patched below

        var preview = Blob(writer, Jpeg(MagickColors.Orange, width, height));
        var thumbnail = Blob(writer, Jpeg(MagickColors.Green, 160, 120));
        var rgb = Blob(writer, [.. Enumerable.Repeat<byte[]>([0, 0, 255], 64 * 42).SelectMany(p => p)]);
        var sensor = Blob(writer, [0xFF, 0xD8, 0xFF, 0xC3, .. new byte[4096]]);

        var exif = Ifd(writer, [(36867, Ascii, AsciiBytes("2008:05:01 10:20:30"))]);
        var ifd0 = Ifd(writer,
        [
            (256, Short, Shorts(width)),
            (257, Short, Shorts(height)),
            (258, Short, Shorts(8, 8, 8)),
            (259, Short, Shorts(6)),
            (271, Ascii, AsciiBytes("Canon")),
            (272, Ascii, AsciiBytes("Canon EOS 350D DIGITAL")),
            (273, Long, Longs(preview.Offset)),
            (274, Short, Shorts(orientation)),
            (277, Short, Shorts(3)),
            (279, Long, Longs(preview.Length)),
            (282, Rational, Longs(72, 1)),
            (283, Rational, Longs(72, 1)),
            (296, Short, Shorts(2)),
            (306, Ascii, AsciiBytes("2008:05:01 10:20:30")),
            (34665, Long, Longs(exif.Offset)),
        ]);
        var ifd1 = Ifd(writer,
        [
            (259, Short, Shorts(6)),
            (513, Long, Longs(thumbnail.Offset)),
            (514, Long, Longs(thumbnail.Length)),
        ]);
        var ifd2 = Ifd(writer,
        [
            (256, Short, Shorts(64)),
            (257, Short, Shorts(42)),
            (258, Short, Shorts(8, 8, 8)),
            (259, Short, Shorts(1)),
            (262, Short, Shorts(2)),
            (273, Long, Longs(rgb.Offset)),
            (277, Short, Shorts(3)),
            (278, Short, Shorts(42)),
            (279, Long, Longs(rgb.Length)),
            (284, Short, Shorts(1)),
        ]);
        var ifd3 = Ifd(writer,
        [
            (259, Short, Shorts(6)),
            (273, Long, Longs(sensor.Offset)),
            (279, Long, Longs(sensor.Length)),
            (50752, Short, Shorts(1, 768, 768)),
        ]);

        Patch(writer, 4, ifd0.Offset);
        Patch(writer, ifd0.Next, ifd1.Offset);
        Patch(writer, ifd1.Next, ifd2.Offset);
        Patch(writer, ifd2.Next, ifd3.Offset);
        Patch(writer, 12, ifd3.Offset);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Jpeg(MagickColor colour, int width, int height)
    {
        using var image = new MagickImage(colour, (uint)width, (uint)height);
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }

    private static (int Offset, int Length) Blob(BinaryWriter writer, byte[] data)
    {
        Align(writer);
        var offset = (int)writer.BaseStream.Position;
        writer.Write(data);
        return (offset, data.Length);
    }

    // Values longer than four bytes go before the IFD; Next is where its link to the following IFD is written.
    private static (int Offset, int Next) Ifd(BinaryWriter writer, (ushort Tag, ushort Type, byte[] Value)[] entries)
    {
        var outOfLine = entries.Select(e => e.Value.Length > 4 ? Blob(writer, e.Value).Offset : -1).ToArray();
        Align(writer);
        var offset = (int)writer.BaseStream.Position;
        writer.Write((ushort)entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var (tag, type, value) = entries[i];
            writer.Write(tag);
            writer.Write(type);
            writer.Write((uint)(value.Length / Size(type)));
            if (outOfLine[i] >= 0)
            {
                writer.Write((uint)outOfLine[i]);
            }
            else
            {
                writer.Write(value);
                writer.Write(new byte[4 - value.Length]);
            }
        }

        var next = (int)writer.BaseStream.Position;
        writer.Write(0u);
        return (offset, next);
    }

    private static void Patch(BinaryWriter writer, int position, int value)
    {
        var end = writer.BaseStream.Position;
        writer.Seek(position, SeekOrigin.Begin);
        writer.Write((uint)value);
        writer.BaseStream.Position = end;
    }

    private static void Align(BinaryWriter writer)
    {
        if (writer.BaseStream.Position % 2 == 1)
        {
            writer.Write((byte)0);
        }
    }

    private static int Size(ushort type) => type switch
    {
        Short => 2,
        Long => 4,
        Rational => 8,
        _ => 1,
    };

    private static byte[] Shorts(params int[] values) => [.. values.SelectMany(v => BitConverter.GetBytes((ushort)v))];

    private static byte[] Longs(params int[] values) => [.. values.SelectMany(v => BitConverter.GetBytes((uint)v))];

    private static byte[] AsciiBytes(string value) => [.. System.Text.Encoding.ASCII.GetBytes(value), 0];
}
