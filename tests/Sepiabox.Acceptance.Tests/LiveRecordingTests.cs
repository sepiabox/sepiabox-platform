using Sepiabox.Tests.Live;

namespace Sepiabox.Acceptance.Tests;

/// <summary>A live run rewrites the recorded responses only when asked and only when every question passed.</summary>
public sealed class LiveRecordingTests
{
    [Fact]
    public void Leaves_the_recorded_file_alone_when_a_question_failed()
    {
        var written = false;

        var line = LiveRecording.RecordIfPassed(requested: true, failures: 2, "/repo/acceptance.recorded.json", () => written = true);

        Assert.False(written);
        Assert.Equal("Not recording: 2 failed, so acceptance.recorded.json is left as it was.", line);
    }

    [Fact]
    public void Records_when_asked_and_every_question_passed()
    {
        var written = false;

        LiveRecording.RecordIfPassed(requested: true, failures: 0, "/repo/acceptance.recorded.json", () => written = true);

        Assert.True(written);
    }

    [Fact]
    public void Does_not_record_when_not_asked()
    {
        var written = false;

        LiveRecording.RecordIfPassed(requested: false, failures: 0, "/repo/acceptance.recorded.json", () => written = true);

        Assert.False(written);
    }
}
