using Sepiabox.Pipeline.Incoming;

namespace Sepiabox.Pipeline.Tests;

public sealed class IncomingFileTests
{
    [Fact]
    public void Round_trips_through_metadata_that_is_ascii_even_for_accents_and_spaces()
    {
        var file = new IncomingFile(
            "gdrive", "Família Costa", "Fotos/Férias 2019/praia.jpg", IncomingMode.Custody, new DateTimeOffset(2019, 7, 14, 10, 30, 0, TimeSpan.Zero),
            "1AbC_d-e", "https://drive.example.com/file/1AbC_d-e");

        var metadata = file.ToMetadata();

        Assert.All(metadata.Values, value => Assert.True(value.All(c => c is >= ' ' and <= '~'), value));
        Assert.True(IncomingFile.TryRead(metadata, out var read, out var problem), problem);
        Assert.Equal(file, read);
    }

    [Fact]
    public void Reads_keys_in_any_case_and_leaves_the_optional_ones_null()
    {
        var metadata = new Dictionary<string, string> { ["SourceType"] = "onedrive", ["SourceName"] = "Ana", ["Path"] = "a\\b.jpg", ["Mode"] = "reference" };

        Assert.True(IncomingFile.TryRead(metadata, out var file, out _));
        Assert.Equal(new IncomingFile("onedrive", "Ana", "a/b.jpg", IncomingMode.Reference), file);
    }

    [Theory]
    [InlineData("sourcetype", "the incoming blob has no 'sourcetype' metadata.")]
    [InlineData("path", "the incoming blob has no 'path' metadata.")]
    [InlineData("mode", "the incoming blob has no 'mode' metadata.")]
    public void Refuses_metadata_with_a_missing_field(string missing, string expected)
    {
        var metadata = new Dictionary<string, string> { ["sourcetype"] = "gdrive", ["sourcename"] = "Ana", ["path"] = "a.jpg", ["mode"] = "custody" };
        metadata.Remove(missing);

        Assert.False(IncomingFile.TryRead(metadata, out _, out var problem));
        Assert.Equal(expected, problem);
    }

    [Theory]
    [InlineData("mode", "keep", "neither custody nor reference")]
    [InlineData("modifiedat", "yesterday", "is not a date")]
    public void Refuses_a_value_it_does_not_understand(string key, string value, string expected)
    {
        var metadata = new Dictionary<string, string> { ["sourcetype"] = "gdrive", ["sourcename"] = "Ana", ["path"] = "a.jpg", ["mode"] = "custody", [key] = value };

        Assert.False(IncomingFile.TryRead(metadata, out _, out var problem));
        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sourcetype")]
    [InlineData("sourcename")]
    [InlineData("path")]
    [InlineData("mode")]
    public void Refuses_a_required_field_whose_value_is_empty(string key)
    {
        var metadata = new Dictionary<string, string> { ["sourcetype"] = "gdrive", ["sourcename"] = "Ana", ["path"] = "a.jpg", ["mode"] = "custody", [key] = "" };

        Assert.False(IncomingFile.TryRead(metadata, out _, out var problem));
        Assert.Equal($"the incoming blob has no '{key}' metadata.", problem);
    }

    [Theory]
    [InlineData(IncomingMode.Custody, "custody")]
    [InlineData(IncomingMode.Reference, "reference")]
    public void Writes_the_mode_as_the_text_a_connector_and_the_worker_agree_on(IncomingMode mode, string text)
    {
        Assert.Equal(text, new IncomingFile("gdrive", "Ana", "a.jpg", mode).ToMetadata()["mode"]);
    }

    [Fact]
    public void Writes_the_modification_time_in_utc_whatever_its_offset()
    {
        var file = new IncomingFile("gdrive", "Ana", "a.jpg", IncomingMode.Custody, new DateTimeOffset(2019, 7, 14, 10, 30, 0, TimeSpan.FromHours(5)));

        Assert.Equal("2019-07-14T05:30:00.0000000Z", file.ToMetadata()["modifiedat"]);
        Assert.True(IncomingFile.TryRead(file.ToMetadata(), out var read, out _));
        Assert.Equal(file.ModifiedAt, read!.ModifiedAt);
    }
}
