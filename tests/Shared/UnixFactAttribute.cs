namespace Sepiabox.Tests.Shared;

/// <summary>A fact that runs shell scripts (stand-ins for FFmpeg, azd or az), or otherwise needs Linux, so it is skipped on Windows.</summary>
internal sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute(string reason = "Runs /bin/sh scripts, which Windows does not have.")
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = reason;
        }
    }
}

/// <summary>A theory that runs shell scripts (stand-ins for FFmpeg, azd or az), so it is skipped on Windows.</summary>
internal sealed class UnixTheoryAttribute : TheoryAttribute
{
    public UnixTheoryAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Runs /bin/sh scripts, which Windows does not have.";
        }
    }
}
