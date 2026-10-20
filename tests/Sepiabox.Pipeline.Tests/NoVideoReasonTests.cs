using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class NoVideoReasonTests
{
    // Work folders store these names; renaming one would make earlier runs' rows unreadable.
    [Fact]
    public void Keeps_the_names_work_folders_store_when_reasons_change() =>
        Assert.Equal(["Unknown", "TrackerModule", "NoStream", "CoverArtOnly"], Enum.GetNames<NoVideoReason>());
}
