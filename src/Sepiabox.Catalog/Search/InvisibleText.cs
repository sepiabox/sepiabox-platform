namespace Sepiabox.Catalog.Search;

/// <summary>
/// Characters that draw nothing or change how text around them is drawn (S-052, D-114): the direction controls (U+200E, U+200F,
/// U+061C, U+202A to U+202E, U+2066 to U+2069) and the zero-width space, word joiner and byte order mark (U+200B, U+2060, U+FEFF). They are
/// taken out of album names when imported and of captions and names when shown, so a name and the link built from it are the same
/// text. The zero-width joiner and non-joiner stay: emoji sequences and some scripts need them.
/// </summary>
public static class InvisibleText
{
    public static string? Strip(string? text) =>
        text is null ? null : new string([.. text.Where(c => !IsInvisible(c))]);

    public static bool IsInvisible(char c) =>
        c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069' or '\u200E' or '\u200F' or '\u061C' or '\u200B' or '\u2060' or '\uFEFF';
}
