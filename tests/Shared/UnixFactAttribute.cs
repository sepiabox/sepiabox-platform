namespace Sepiabox.Tests.Shared;

/// <summary>A fact that runs shell-script stand-ins for FFmpeg, so it is skipped on Windows.</summary>
internal sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Uses /bin/sh scripts in place of ffmpeg and ffprobe.";
        }
    }
}

/// <summary>A theory that runs shell-script stand-ins for FFmpeg, so it is skipped on Windows.</summary>
internal sealed class UnixTheoryAttribute : TheoryAttribute
{
    public UnixTheoryAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Uses /bin/sh scripts in place of ffmpeg and ffprobe.";
        }
    }
}
