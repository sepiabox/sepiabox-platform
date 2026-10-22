namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Where <see cref="CaptureDate.Value"/> came from, written to the manifest as <c>capturedAtSource</c>. How they rank
/// is decided in <see cref="CaptureDate.Rank"/>, not by their order here.
/// </summary>
public enum CaptureDateSource
{
    /// <summary>The parent folder's name, at midnight.</summary>
    Folder,

    /// <summary>A date and time in the file name, or a time in the file name on the day its folder is named after.</summary>
    FileName,

    /// <summary>What an export of the photos says about the file (S-045, D-053): a Takeout sidecar, an Instagram post list.</summary>
    Export,

    /// <summary>The file's own metadata (EXIF, QuickTime).</summary>
    Metadata,
}

/// <summary>When a photo or video was taken, as wall-clock time (D-037), and how that is known.</summary>
public sealed record CaptureDate(DateTime Value, CaptureDateSource Source)
{
    /// <summary>
    /// Earliest plausible date written by a device: cameras whose clock was never set write dates like 0000:00:00,
    /// 1980-01-01 or 1904 (the QuickTime epoch), in their metadata and in the file names they choose alike.
    /// </summary>
    public static readonly DateTime EarliestPlausible = new(1990, 1, 1);

    /// <summary>Earliest plausible folder date: a person typed it, so a scan of a 1978 print in <c>1978-08-15</c> keeps it.</summary>
    public static readonly DateTime EarliestPlausibleFolder = new(1900, 1, 1);

    /// <summary>
    /// The capture date of the file at <paramref name="path"/> (D-046): its metadata date when plausible, otherwise
    /// the first plausible date its path gives (<see cref="PathDate"/>), otherwise null. The one place where a date is
    /// decided, used by <c>scan</c>, <c>process</c> and every later manifest writer, so they all agree.
    /// </summary>
    /// <param name="fromMetadata">What the file's metadata says, as written (<see cref="MediaMetadata.RawCapturedAt"/>).</param>
    /// <param name="path">The file's path, with either separator; only its name and parent folder are read.</param>
    /// <param name="clock">Gives "now" for the plausibility window.</param>
    /// <param name="fromExport">
    /// What an export says, as wall-clock time (S-045): between the file's metadata and its name, since an export knows more
    /// than a name does and less than the camera.
    /// </param>
    public static CaptureDate? Resolve(DateTime? fromMetadata, string path, TimeProvider clock, DateTime? fromExport = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (fromMetadata is { } metadata && IsPlausible(metadata, EarliestPlausible, clock))
        {
            return new CaptureDate(metadata, CaptureDateSource.Metadata);
        }

        if (fromExport is { } exported && IsPlausible(exported, EarliestPlausible, clock))
        {
            return new CaptureDate(exported, CaptureDateSource.Export);
        }

        return PathDate.Candidates(path)
            .FirstOrDefault(candidate => IsPlausible(
                candidate.Date.Value, candidate.DayFromFolder ? EarliestPlausibleFolder : EarliestPlausible, clock))
            ?.Date;
    }

    /// <summary>
    /// Not before <paramref name="earliest"/> (<see cref="EarliestPlausibleFolder"/> when the day comes from a folder,
    /// otherwise <see cref="EarliestPlausible"/>) and not more than a day after now. The extra day covers a camera set
    /// to a time zone ahead of the computer's.
    /// </summary>
    private static bool IsPlausible(DateTime value, DateTime earliest, TimeProvider clock)
    {
        return value >= earliest && value <= clock.GetLocalNow().DateTime.AddDays(1);
    }

    /// <summary>
    /// The date to keep when two descriptions of the same bytes disagree (D-046): the stronger source wins (metadata,
    /// then export, then file name, then folder, then no date at all). Between two path dates of the same source, the earlier
    /// date, so the result does not depend on which copy was seen first. Between two metadata dates, which only
    /// differ when the description itself is replaced, <paramref name="preferred"/>.
    /// </summary>
    public static CaptureDate? Stronger(CaptureDate? preferred, CaptureDate? other)
    {
        if (preferred is null || other is null)
        {
            return preferred ?? other;
        }

        if (preferred.Source != other.Source)
        {
            return Rank(preferred.Source) > Rank(other.Source) ? preferred : other;
        }

        if (preferred.Source == CaptureDateSource.Metadata)
        {
            return preferred;
        }

        return other.Value < preferred.Value ? other : preferred;
    }

    /// <summary>Metadata is written by the device at capture; an export knows the date better than a file name does; a time in a file name says more than a folder's day.</summary>
    public static int Rank(CaptureDateSource source) => source switch
    {
        CaptureDateSource.Folder => 1,
        CaptureDateSource.FileName => 2,
        CaptureDateSource.Export => 3,
        CaptureDateSource.Metadata => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown capture date source."),
    };
}
