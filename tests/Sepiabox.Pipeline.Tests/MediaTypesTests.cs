using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class MediaTypesTests
{
    [Theory]
    [InlineData("IMG_0001.JPG", MediaKind.Photo)]
    [InlineData("photo.heic", MediaKind.Photo)]
    [InlineData("DSC_0042.NEF", MediaKind.Photo)]
    [InlineData("clip.MOV", MediaKind.Video)]
    [InlineData("party.mp4", MediaKind.Video)]
    [InlineData("notes.txt", MediaKind.Other)]
    [InlineData("Thumbs.db", MediaKind.Other)]
    [InlineData("no-extension", MediaKind.Other)]
    [InlineData("._IMG_0001.JPG", MediaKind.Other)]
    public void Classify_uses_the_file_extension(string fileName, MediaKind expected) =>
        Assert.Equal(expected, MediaTypes.Classify(Path.Combine("folder", fileName)));

    [Theory]
    [InlineData("MOV001.MOD", "video/mpeg")]
    [InlineData("MOV001.TOD", "video/mp2t")]
    [InlineData("00001.MTS", "video/mp2t")]
    [InlineData("HDV_0001.m2t", "video/mp2t")]
    [InlineData("VTS_01_1.VOB", "video/mpeg")]
    [InlineData("tape.dv", "video/x-dv")]
    [InlineData("MVI_0001.MPG", "video/mpeg")]
    public void Classifies_camcorder_and_dvd_files_as_video_when_the_extension_is_known(string fileName, string contentType)
    {
        Assert.Equal(MediaKind.Video, MediaTypes.Classify(Path.Combine("folder", fileName)));
        Assert.Equal(contentType, MediaTypes.ContentType(fileName));
    }

    [Fact]
    public void Classifies_canon_raw_files_as_photos_when_the_extension_is_cr2() =>
        Assert.Equal(MediaKind.Photo, MediaTypes.Classify(Path.Combine("folder", "IMG_0001.CR2")));
}
