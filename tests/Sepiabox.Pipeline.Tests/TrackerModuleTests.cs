using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class TrackerModuleTests
{
    [Theory]
    [InlineData("M.K.")]
    [InlineData("M!K!")]
    [InlineData("M&K!")]
    [InlineData("4CHN")]
    [InlineData("6CHN")]
    [InlineData("8CHN")]
    [InlineData("FLT4")]
    [InlineData("FLT8")]
    [InlineData("CD81")]
    [InlineData("OKTA")]
    [InlineData("16CH")]
    [InlineData("32CN")]
    public void Recognises_a_tracker_module_when_the_tag_at_1080_is_a_protracker_family_one(string tag)
    {
        using var temp = new TempFolder();

        Assert.True(TrackerModule.IsTrackerModule(temp.Write("song.mod", TestTrackerModules.Module(tag))));
    }

    [Theory]
    [InlineData("\0\0\x01\xBA")]
    [InlineData("MK..")]
    [InlineData("ACHN")]
    [InlineData("1XCH")]
    [InlineData("0CHN")]
    [InlineData("00CH")]
    [InlineData("05CN")]
    public void Does_not_recognise_a_module_when_the_tag_is_anything_else(string tag)
    {
        using var temp = new TempFolder();

        Assert.False(TrackerModule.IsTrackerModule(temp.Write("MOV001.MOD", TestTrackerModules.Module(tag))));
    }

    [Fact]
    public void Does_not_recognise_a_module_when_the_file_starts_with_an_mpeg_pack_header()
    {
        var module = TestTrackerModules.Module("M.K.");
        module[0] = 0x00;
        module[1] = 0x00;
        module[2] = 0x01;
        module[3] = 0xBA;

        using var stream = new MemoryStream(module);
        Assert.False(TrackerModule.IsMatch(stream));
    }

    [Fact]
    public void Does_not_recognise_a_module_when_the_file_starts_with_any_mpeg_start_code()
    {
        var module = TestTrackerModules.Module("M.K.");
        module[0] = 0x00;
        module[1] = 0x00;
        module[2] = 0x01;
        module[3] = 0xB3;

        using var stream = new MemoryStream(module);
        Assert.False(TrackerModule.IsMatch(stream));
    }

    [Fact]
    public void Puts_the_stream_position_back_when_it_has_looked()
    {
        using var stream = new MemoryStream(TestTrackerModules.Module("M.K."));
        stream.Position = 42;

        Assert.True(TrackerModule.IsMatch(stream));
        Assert.Equal(42, stream.Position);
    }

    [Fact]
    public void Does_not_recognise_a_module_when_the_stream_cannot_seek()
    {
        using var stream = new ForwardOnlyStream(TestTrackerModules.Module("M.K."));

        Assert.False(TrackerModule.IsMatch(stream));
    }

    [Fact]
    public void Does_not_recognise_a_module_when_the_file_has_transport_stream_sync_bytes()
    {
        var module = TestTrackerModules.Module("M.K.");
        module[0] = 0x47;
        module[188] = 0x47;

        using var stream = new MemoryStream(module);
        Assert.False(TrackerModule.IsMatch(stream));
    }

    [Fact]
    public void Recognises_a_module_whose_title_starts_with_the_sync_byte_value_when_the_next_packet_has_none()
    {
        var module = TestTrackerModules.Module("M.K.");
        module[0] = (byte)'G';

        using var stream = new MemoryStream(module);
        Assert.True(TrackerModule.IsMatch(stream));
    }

    [Theory]
    [InlineData(1083, false)]
    [InlineData(1084, true)]
    public void Needs_the_whole_tag_when_the_file_ends_near_it(int length, bool expected)
    {
        var module = TestTrackerModules.Module("M.K.")[..length];

        using var stream = new MemoryStream(module);
        Assert.Equal(expected, TrackerModule.IsMatch(stream));
    }

    [Fact]
    public void Only_checks_mod_files_when_given_a_path()
    {
        using var temp = new TempFolder();

        Assert.False(TrackerModule.IsTrackerModule(temp.Write("song.mpg", TestTrackerModules.Module("M.K."))));
    }

    [Fact]
    public void Says_no_when_the_file_cannot_be_read()
    {
        using var temp = new TempFolder();
        // A folder named like a module cannot be opened as a file.
        var folder = Path.Combine(temp.Path, "song.mod");
        Directory.CreateDirectory(folder);

        Assert.False(TrackerModule.IsTrackerModule(folder));
        Assert.False(TrackerModule.IsTrackerModule(Path.Combine(temp.Path, "missing.mod")));
    }
}
