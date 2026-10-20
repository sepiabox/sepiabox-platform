using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class TrackerModuleTests
{
    [Theory]
    [InlineData("M.K.")]
    [InlineData("M!K!")]
    [InlineData("4CHN")]
    [InlineData("6CHN")]
    [InlineData("8CHN")]
    [InlineData("FLT4")]
    [InlineData("FLT8")]
    [InlineData("16CH")]
    [InlineData("32CN")]
    public void Recognises_a_tracker_module_when_the_tag_at_1080_is_a_protracker_family_one(string tag)
    {
        using var temp = new TempFolder();

        Assert.True(TrackerModule.IsMatch(temp.Write("song.mod", TestTrackerModules.Module(tag))));
    }

    [Theory]
    [InlineData("\0\0\x01\xBA")]
    [InlineData("MK..")]
    [InlineData("ACHN")]
    [InlineData("1XCH")]
    public void Does_not_recognise_a_module_when_the_tag_is_anything_else(string tag)
    {
        using var temp = new TempFolder();

        Assert.False(TrackerModule.IsMatch(temp.Write("MOV001.MOD", TestTrackerModules.Module(tag))));
    }

    [Fact]
    public void Does_not_recognise_a_module_when_the_file_is_shorter_than_the_tag()
    {
        using var temp = new TempFolder();

        Assert.False(TrackerModule.IsMatch(temp.Write("tiny.mod", new byte[1082])));
    }
}
