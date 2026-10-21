using Sepiabox.Ingest.Scanning;

namespace Sepiabox.Ingest.Processing;

/// <summary>
/// Prints the videos whose 720p version was made without sound in a run, next to the files left out: only those whose
/// sound stream does not decode a single frame (D-084, D-087), each with FFmpeg's message for the transcode with sound.
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
            ? "1 video's 720p version was made without sound, because FFmpeg cannot decode its sound:"
            : $"{withoutSound.Count:N0} videos' 720p versions were made without sound, because FFmpeg cannot decode their sound:");
        foreach (var video in withoutSound)
        {
            output.WriteLine($"  Made without sound {video.Path}: {video.Reason}");
        }
    }
}
