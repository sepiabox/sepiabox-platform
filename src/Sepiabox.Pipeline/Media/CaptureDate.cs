namespace Sepiabox.Pipeline.Media;

/// <summary>Where <see cref="CaptureDate.Value"/> came from, written to the manifest as <c>capturedAtSource</c>.</summary>
public enum CaptureDateSource
{
    /// <summary>The file's own metadata (EXIF, QuickTime).</summary>
    Metadata,

    /// <summary>A date and time in the file name, or a time in the file name on the day its folder is named after.</summary>
    FileName,

    /// <summary>The parent folder's name, at midnight.</summary>
    Folder,
}

/// <summary>When a photo or video was taken, as wall-clock time (D-037), and how that is known.</summary>
public sealed record CaptureDate(DateTime Value, CaptureDateSource Source)
{
    // Cameras whose clock was never set write dates like 0000:00:00, 1980-01-01 or 1904 (the QuickTime epoch).
    public static readonly DateTime EarliestPlausible = new(1990, 1, 1);

    /// <summary>
    /// The capture date of the file at <paramref name="path"/> (D-046): its metadata date when plausible, otherwise
    /// the first plausible date its path gives (<see cref="PathDate"/>), otherwise null. The one place where both
    /// <c>scan</c> and <c>process</c> decide a date, so the scan's years match what process records.
    /// </summary>
    /// <param name="fromMetadata">What the file's metadata says, unfiltered.</param>
    /// <param name="path">The file's path, with either separator; only its name and parent folder are read.</param>
    /// <param name="clock">Gives "now" for the plausibility window.</param>
    public static CaptureDate? Resolve(DateTime? fromMetadata, string path, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (fromMetadata is { } metadata && IsPlausible(metadata, clock))
        {
            return new CaptureDate(metadata, CaptureDateSource.Metadata);
        }

        return PathDate.Candidates(path).FirstOrDefault(candidate => IsPlausible(candidate.Value, clock));
    }

    /// <summary>
    /// Not before <see cref="EarliestPlausible"/> and not more than a day after now: a camera or phone with a reset
    /// clock writes such dates in its metadata and its file names alike. The extra day covers a camera set to a time
    /// zone ahead of the computer's.
    /// </summary>
    public static bool IsPlausible(DateTime value, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return value >= EarliestPlausible && value <= clock.GetLocalNow().DateTime.AddDays(1);
    }
}
