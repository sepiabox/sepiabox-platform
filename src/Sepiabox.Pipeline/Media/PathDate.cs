using System.Globalization;
using System.Text.RegularExpressions;

namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Reads when a file was taken from its path, for files whose own metadata says nothing (D-046): phones name
/// files after the capture time (<c>IMG_20120304_101112.jpg</c>) and camcorder archives are often kept in folders
/// named after the capture day (<c>2011-09-25\MNU_110425_d8b2.MTS</c>). Pure: the file is never opened.
/// </summary>
public static partial class PathDate
{
    private const string FolderFormat = "yyyy-MM-dd";

    /// <summary>
    /// Every date the path gives, in order of precedence: the file name, the folder with a time from the file name,
    /// then the folder alone. Empty when nothing in the path is a valid date and time. All of them, so a caller that
    /// rejects one (an implausible date) can fall back to the next. Only the parent folder counts: a dated folder
    /// higher up says less about the file.
    /// </summary>
    public static IEnumerable<PathDateCandidate> Candidates(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(path);
    }

    private static IEnumerable<PathDateCandidate> Parse(string path)
    {
        // Both separators on every OS: paths in manifests use '/', paths typed on Windows use '\'. On Linux '\' is a
        // legal file name character, but a name containing one is far rarer than a Windows path given on Linux.
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            yield break;
        }

        var fileName = segments[^1];
        foreach (Match match in FileNameDateTime().Matches(fileName))
        {
            if (TryParseExact($"{match.Groups["date"].Value}{match.Groups["time"].Value}", "yyyyMMddHHmmss", out var taken))
            {
                yield return new PathDateCandidate(new CaptureDate(taken, CaptureDateSource.FileName), DayFromFolder: false);
                break;
            }
        }

        if (segments.Length < 2 || !TryParseExact(segments[^2], FolderFormat, out var day))
        {
            yield break;
        }

        var camcorder = CamcorderTime().Match(fileName);
        if (camcorder.Success && TryParseExact(camcorder.Groups["time"].Value, "HHmmss", out var time))
        {
            yield return new PathDateCandidate(new CaptureDate(day.Add(time.TimeOfDay), CaptureDateSource.FileName), DayFromFolder: true);
        }

        yield return new PathDateCandidate(new CaptureDate(day, CaptureDateSource.Folder), DayFromFolder: true);
    }

    private static bool TryParseExact(string value, string format, out DateTime result) =>
        DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    // ASCII digits only. The date may follow a letter (IMG20120304_101112.jpg on some Android phones) but not a digit,
    // so the end of a longer number (120120304_101112) is not read as a date. The time may go on: Pixel phones add
    // milliseconds (PXL_20230115_143022123.jpg).
    [GeneratedRegex(@"(?<![0-9])(?<date>[0-9]{8})[_-](?<time>[0-9]{6})")]
    private static partial Regex FileNameDateTime();

    // Some camcorders name their clips MNU_<HHMMSS>_..., leaving the day to the folder they are copied into.
    [GeneratedRegex(@"^MNU_(?<time>[0-9]{6})(?![0-9])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CamcorderTime();
}
