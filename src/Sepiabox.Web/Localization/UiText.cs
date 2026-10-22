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

    /// <summary>An occasion type's name in the current culture (S-024): "Wedding", "Casamento"; the key itself when no text names it.</summary>
    public static string EventTypeLabel(this IStringLocalizer<SharedResource> text, string type)
    {
        var label = text["EventType_" + type];
        return label.ResourceNotFound ? type : label.Value;
    }

    /// <summary>"Photo" or "Video".</summary>
    public static string KindLabel(this IStringLocalizer<SharedResource> text, string kind) =>
        AssetKinds.IsVideo(kind) ? text["Kind_Video"] : text["Kind_Photo"];

    /// <summary>
    /// An occasion's title (S-023): "Lisbon · Dec 14–16, 2024" in English, "Lisbon · 14–16 dez 2024" in Brazilian
    /// Portuguese. The place is as the catalog stored it and is left out when there is none; the date range is written
    /// in the current culture, never stored (D-050), with the shortest form its days allow: one day, days of one month,
    /// of one year, or of two years.
    /// </summary>
    public static string OccasionTitle(this IStringLocalizer<SharedResource> text, string? place, DateTime startsAt, DateTime endsAt)
    {
        // Abbreviated month names without the dot ICU gives some cultures ("dez." becomes "dez"): the patterns do the rest.
        var format = (DateTimeFormatInfo)CultureInfo.CurrentCulture.DateTimeFormat.Clone();
        format.AbbreviatedMonthNames = [.. format.AbbreviatedMonthNames.Select(name => name.TrimEnd('.'))];
        format.AbbreviatedMonthGenitiveNames = [.. format.AbbreviatedMonthGenitiveNames.Select(name => name.TrimEnd('.'))];

        string Part(string key, DateTime day) => day.ToString(text[key], format);
        var (first, last) = (startsAt.Date, endsAt.Date);
        var range = first == last ? Part("Format_Occasion_Day", first)
            : first.Year == last.Year && first.Month == last.Month ? Part("Format_Occasion_MonthStart", first) + "–" + Part("Format_Occasion_MonthEnd", last)
            : first.Year == last.Year ? Part("Format_Occasion_YearStart", first) + " – " + Part("Format_Occasion_Day", last)
            : Part("Format_Occasion_Day", first) + " – " + Part("Format_Occasion_Day", last);
        return string.IsNullOrWhiteSpace(place) ? range : place + " · " + range;
    }
}
