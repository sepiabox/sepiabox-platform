using ImageMagick;

namespace Sepiabox.Tests.Shared;

/// <summary>Generates small JPEGs with the metadata a phone writes, so tests need no committed fixtures.</summary>
internal static class TestPhotos
{
    /// <summary>
    /// A 64×48 JPEG taken on 2019-07-14 16:30:05 with an iPhone 8 at 41.18 N, 8.69 W (Porto).
    /// <paramref name="color"/> changes the pixels, and therefore the hash.
    /// </summary>
    public static byte[] Porto(string color = "skyblue")
    {
        using var image = new MagickImage(new MagickColor(color), 64, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "Apple");
        exif.SetValue(ExifTag.Model, "iPhone 8");
        exif.SetValue(ExifTag.DateTimeOriginal, "2019:07:14 16:30:05");
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(41, 1), new Rational(10, 1), new Rational(48, 1)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "W");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(8, 1), new Rational(41, 1), new Rational(24, 1)]);
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>A 64×48 JPEG whose EXIF says it was taken at <paramref name="dateTimeOriginal"/> ("yyyy:MM:dd HH:mm:ss").</summary>
    public static byte[] Taken(string dateTimeOriginal, string color = "khaki")
    {
        using var image = new MagickImage(new MagickColor(color), 64, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, dateTimeOriginal);
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>A solid-colour JPEG of the given size, optionally with an EXIF orientation (6 = rotate 90° clockwise).</summary>
    public static byte[] Sized(int width, int height, ushort? orientation = null)
    {
        using var image = new MagickImage(MagickColors.Orange, (uint)width, (uint)height);
        if (orientation is { } value)
        {
            // ImageMagick writes the EXIF orientation from the image's own attribute, so set both.
            image.Orientation = (Orientation)value;
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, value);
            image.SetProfile(exif);
        }

        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>A 32×32 PNG with no metadata at all.</summary>
    public static byte[] Plain()
    {
        using var image = new MagickImage(MagickColors.Gray, 32, 32);
        return image.ToByteArray(MagickFormat.Png);
    }
}
