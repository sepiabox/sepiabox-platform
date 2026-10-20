using System.Text;

namespace Sepiabox.Tests.Shared;

internal static class TestTrackerModules
{
    /// <summary>
    /// A minimal ProTracker module: a 20-byte title, 31 sample headers of 30 bytes, song length, restart byte,
    /// a 128-entry pattern order table, the 4-byte format tag at offset 1080, then one 1024-byte pattern
    /// (64 rows of 4 channels).
    /// </summary>
    public static byte[] Module(string tag = "M.K.")
    {
        var module = new byte[1084 + 1024];
        Encoding.ASCII.GetBytes("sepiabox test song").CopyTo(module, 0);
        module[950] = 1;
        module[951] = 127;
        Encoding.Latin1.GetBytes(tag).CopyTo(module, 1080);
        return module;
    }
}
