namespace Sepiabox.Web.Services;

/// <summary>
/// Portuguese date text for the UI. Written out because the app runs with invariant globalization
/// (no culture data), so <c>CultureInfo("pt-PT")</c> would not give Portuguese month names.
/// </summary>
public static class PortugueseDates
{
    private static readonly string[] s_months =
    [
        "janeiro", "fevereiro", "março", "abril", "maio", "junho",
        "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
    ];

    /// <summary>"julho de 2019".</summary>
    public static string MonthAndYear(int year, int month) => $"{s_months[month - 1]} de {year}";

    /// <summary>"14 de julho de 2019, 10:32".</summary>
    public static string DateAndTime(DateTime value) =>
        $"{value.Day} de {s_months[value.Month - 1]} de {value.Year}, {value:HH\\:mm}";
}
