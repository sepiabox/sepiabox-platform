namespace Sepiabox.Web.Services;

/// <summary>What the page does to text the family or an export wrote, before it shows it (S-052).</summary>
public static class TextSafety
{
    /// <summary>
    /// The text without the characters that change the direction text is drawn in, and the zero-width ones (<see cref="Sepiabox.Catalog.Search.InvisibleText"/>):
    /// in a caption they could make what follows read in another order, or look like part of the page. Null stays null.
    /// </summary>
    public static string? WithoutBidiControls(string? text) => Sepiabox.Catalog.Search.InvisibleText.Strip(text);
}
