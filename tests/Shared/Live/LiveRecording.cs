namespace Sepiabox.Tests.Live;

/// <summary>
/// When a live run (Category=LiveApi) rewrites the recorded responses CI replays: only when asked with
/// <c>SEPIABOX_RECORD_GOLDEN=1</c> and only when every question passed, retries allowed. A failing run leaves the file
/// as it was, so a half-wrong recording can never be committed by mistake.
/// </summary>
internal static class LiveRecording
{
    public const string Variable = "SEPIABOX_RECORD_GOLDEN";

    public static bool Requested => Environment.GetEnvironmentVariable(Variable) == "1";

    /// <summary>Calls <paramref name="write"/> when <paramref name="requested"/> and nothing failed; returns the line to print.</summary>
    public static string RecordIfPassed(bool requested, int failures, string path, Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (!requested)
        {
            return $"Not recording: {Variable} is not 1.";
        }

        if (failures > 0)
        {
            return $"Not recording: {failures} failed, so {Path.GetFileName(path)} is left as it was.";
        }

        write();
        return $"Recorded {path}.";
    }
}
