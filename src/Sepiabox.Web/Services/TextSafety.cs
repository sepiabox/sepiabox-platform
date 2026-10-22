namespace Sepiabox.Web.Services;

/// <summary>What the page does to text the family or an export wrote, before it shows it (S-052).</summary>
public static class TextSafety
{
    /// <summary>
    /// The text without the characters that change the direction text is drawn in (U+202A to U+202E, U+2066 to U+2069, U+200E, U+200F and
    /// U+061C): in a caption they could make what follows read in another order, or look like part of the page. Null stays null.
    /// </summary>
    public static string? WithoutBidiControls(string? text) =>
        text is null ? null : new string([.. text.Where(c => !IsBidiControl(c))]);

    private static bool IsBidiControl(char c) =>
        c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069' or '\u200E' or '\u200F' or '\u061C';
}
