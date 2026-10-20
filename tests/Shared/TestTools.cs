namespace Sepiabox.Tests.Shared;

internal static class TestTools
{
    /// <summary>
    /// Writes an executable <c>/bin/sh</c> script named <paramref name="name"/>, standing in for ffmpeg or ffprobe.
    /// Only <see cref="UnixFactAttribute"/> and <see cref="UnixTheoryAttribute"/> tests call this.
    /// </summary>
    public static string FakeTool(TempFolder folder, string name, string script)
    {
        var path = Path.Combine(folder.Path, name);
        File.WriteAllText(path, "#!/bin/sh\n" + script + "\n");
        // The guard keeps the Windows build free of platform warnings; those tests are skipped there.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
