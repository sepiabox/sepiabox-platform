using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Processing;

/// <summary>A file with a video extension that <c>process</c> left out because it holds no video (D-045).</summary>
/// <param name="Path">The file on the drive.</param>
/// <param name="Reason">Why; null for a file left out by a build that did not record the reason.</param>
/// <param name="Earlier">True when an earlier run left it out and it is unchanged, so this run did not read it.</param>
public sealed record LeftOutFile(string Path, NoVideoReason? Reason, bool Earlier);

/// <summary>
/// Prints the files a run left out, next to its problems: each file found this time with its reason, and those
/// left out earlier as a count, since they would repeat on every run of a large drive.
/// </summary>
public static class LeftOutReport
{
    public const string RetryHint = "run process with --retry-left-out to read them again, or --list-left-out to list them";

    public static void Write(IReadOnlyList<LeftOutFile> leftOut, bool listEarlier, TextWriter output)
    {
        foreach (var file in leftOut.Where(f => !f.Earlier || listEarlier))
        {
            output.WriteLine($"  Left out {file.Path}: {Describe(file.Reason)}{(file.Earlier ? " (earlier run)" : "")}");
        }

        var earlier = leftOut.Count(f => f.Earlier);
        if (earlier > 0)
        {
            output.WriteLine(earlier == 1
                ? $"1 file left out by an earlier run is unchanged and was not read again; {RetryHint}."
                : $"{earlier:N0} files left out by earlier runs are unchanged and were not read again; {RetryHint}.");
        }
    }

    public static string Describe(NoVideoReason? reason) => reason switch
    {
        NoVideoReason.TrackerModule => "tracker-music module (ProTracker format tag at offset 1080), not a video",
        NoVideoReason.NoStream => "no video stream, even with a longer probe",
        NoVideoReason.CoverArtOnly => "only cover art, no video stream",
        _ => "no video (reason not recorded)",
    };
}
