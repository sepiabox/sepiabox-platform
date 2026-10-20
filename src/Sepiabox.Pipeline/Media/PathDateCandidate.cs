namespace Sepiabox.Pipeline.Media;

/// <summary>A date read from a path.</summary>
/// <param name="Date">The date and how it was read.</param>
/// <param name="DayFromFolder">
/// The day comes from a folder name a person typed, so it may be older than any device clock (D-046).
/// </param>
public sealed record PathDateCandidate(CaptureDate Date, bool DayFromFolder);
