using System.Globalization;

namespace Sepiabox.Web.Services;

/// <summary>
/// A face shown from its asset's thumbnail (S-019): <see cref="Box"/> is <c>[left, top, width, height]</c> relative to
/// the upright derivatives, mapped with the thumbnail's own <see cref="ThumbnailWidth"/> and
/// <see cref="ThumbnailHeight"/>, never the original's size (D-047).
/// </summary>
public sealed record FaceCrop(string AssetId, IReadOnlyList<double> Box, int ThumbnailWidth, int ThumbnailHeight)
{
    /// <summary>Room around the face, so a crop shows a little hair and chin rather than only the detector's box.</summary>
    public const double Margin = 1.3;

    /// <summary>
    /// The CSS that places the whole thumbnail inside a square frame so the face fills it: the image's width, height,
    /// left and top as percentages of the frame. The square is the face's longer side with <see cref="Margin"/>,
    /// centred on the face and kept inside the thumbnail.
    /// </summary>
    public string ImageStyle()
    {
        double width = ThumbnailWidth, height = ThumbnailHeight;
        var faceWidth = Math.Clamp(Box[2], 0, 1) * width;
        var faceHeight = Math.Clamp(Box[3], 0, 1) * height;
        var side = Math.Min(Math.Max(Math.Max(faceWidth, faceHeight) * Margin, 1), Math.Min(width, height));
        var centreX = (Math.Clamp(Box[0], 0, 1) * width) + (faceWidth / 2);
        var centreY = (Math.Clamp(Box[1], 0, 1) * height) + (faceHeight / 2);
        var left = Math.Clamp(centreX - (side / 2), 0, width - side);
        var top = Math.Clamp(centreY - (side / 2), 0, height - side);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"width:{Percent(width / side)};height:{Percent(height / side)};left:{Percent(-left / side)};top:{Percent(-top / side)}");
    }

    // Rounded first and then 0.0 added, so a crop at the very edge (-0.0, or a tiny negative) is written "0", not "-0".
    private static string Percent(double fraction) =>
        (Math.Round(fraction * 100, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture) + "%";
}
