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
    [InlineData("MOV001.MOD", MediaKind.Video)]
    [InlineData("notes.txt", MediaKind.Other)]
    [InlineData("Thumbs.db", MediaKind.Other)]
    [InlineData("no-extension", MediaKind.Other)]
    [InlineData("._IMG_0001.JPG", MediaKind.Other)]
    public void Classify_uses_the_file_extension(string fileName, MediaKind expected) =>
        Assert.Equal(expected, MediaTypes.Classify(Path.Combine("folder", fileName)));
}
