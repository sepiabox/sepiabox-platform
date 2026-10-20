using System.Globalization;
using Microsoft.Extensions.Localization;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Localization;

/// <summary>
/// UI text that is more than one resource: dates, asset kinds and the "no date" fallback, all in the
/// current culture (D-050). The culture and the UI culture are always the same (<see cref="OneCultureCookieProvider"/>),
/// so a date's pattern (a resource) and its month names (the culture) come from one language.
/// </summary>
public static class UiText
{
    /// <summary>"July 2019" in English; the pattern is a resource, the month name comes from the culture.</summary>
    public static string MonthAndYear(this IStringLocalizer<SharedResource> text, DateOnly month) =>
        month.ToString(text["Format_MonthAndYear"], CultureInfo.CurrentCulture);

    /// <summary>"July 14, 2019, 10:32" in English; the pattern is a resource, the month name comes from the culture.</summary>
    public static string DateAndTime(this IStringLocalizer<SharedResource> text, DateTime value) =>
        value.ToString(text["Format_DateAndTime"], CultureInfo.CurrentCulture);

    /// <summary>The date and time, or "No date" for an asset without one.</summary>
    public static string DateOrNoDate(this IStringLocalizer<SharedResource> text, DateTime? value) =>
        value is { } at ? text.DateAndTime(at) : text["NoDate"];

    /// <summary>"Photo" or "Video".</summary>
    public static string KindLabel(this IStringLocalizer<SharedResource> text, string kind) =>
        AssetKinds.IsVideo(kind) ? text["Kind_Video"] : text["Kind_Photo"];
}
