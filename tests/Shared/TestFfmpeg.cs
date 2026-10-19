namespace Sepiabox.Tests.Shared;

internal static class TestFfmpeg
{
    /// <summary>
    /// A folder that never holds ffmpeg, for tests that must behave the same whether or not FFmpeg is
    /// installed: videos stay pending and photos get thumbnails.
    /// </summary>
    public static string None { get; } = Path.Combine(Path.GetTempPath(), "sepiabox-tests-no-ffmpeg");
}
