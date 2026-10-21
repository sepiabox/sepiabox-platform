using Sepiabox.Ingest.Scanning;

namespace Sepiabox.Ingest.Processing;

/// <summary>
/// Prints the videos whose 720p version was made without sound in a run (D-084), next to the files left out: the
/// fallback follows any failed transcode, so each one is shown with FFmpeg's message and can be checked.
/// </summary>
public static class WithoutSoundReport
{
    public static void Write(IReadOnlyList<ScanProblem> withoutSound, TextWriter output)
    {
        if (withoutSound.Count == 0)
        {
            return;
        }

        output.WriteLine(withoutSound.Count == 1
            ? "1 video's 720p version was made without sound, because the transcode with sound failed:"
            : $"{withoutSound.Count:N0} videos' 720p versions were made without sound, because the transcode with sound failed:");
        foreach (var video in withoutSound)
        {
            output.WriteLine($"  Made without sound {video.Path}: {video.Reason}");
        }
    }
}
