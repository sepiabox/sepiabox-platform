using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Exports;

/// <summary>
/// Reads the <c>.xmp</c> sidecar the Photos app writes with "Export IPTC as XMP" (S-047, D-109): the description
/// (<c>dc:description</c>, else <c>dc:title</c>), the date (<c>photoshop:DateCreated</c>, else <c>exif:DateTimeOriginal</c>)
/// and the GPS position. Keywords and everything else are ignored. A property may be an element or an attribute.
/// </summary>
public static partial class AppleXmp
{
    private static readonly XNamespace s_dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace s_photoshop = "http://ns.adobe.com/photoshop/1.0/";
    private static readonly XNamespace s_exif = "http://ns.adobe.com/exif/1.0/";
    private static readonly XNamespace s_xml = XNamespace.Xml;

    // 2018-05-23, 2018-05-23T16:16, 2018-05-23T16:16:45, with fraction, and Z or an offset.
    [GeneratedRegex(@"^(\d{4})-(\d\d)-(\d\d)(?:[T ](\d\d):(\d\d)(?::(\d\d)(?:[.,]\d+)?)?)?\s*(Z|[+-]\d\d(?::?\d\d)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Iso();

    // 38,43.338N or 38,43,20.1N or 38.7223N or -9.1393: degrees, then minutes and seconds if any, then the hemisphere.
    [GeneratedRegex(@"^\s*(-?\d+(?:\.\d+)?)(?:,(\d+(?:\.\d+)?))?(?:,(\d+(?:\.\d+)?))?\s*([NSEW])?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Coordinate();

    /// <summary>What the sidecar says; null when the text is not XML. The text is never fetched from anywhere: DTDs are refused.</summary>
    public static ExportMetadata? Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        XDocument document;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 };
            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        var root = document.Root;
        if (root is null)
        {
            return null;
        }

        var caption = LanguageText(root, s_dc + "description") ?? LanguageText(root, s_dc + "title");
        var date = Date(Value(root, s_photoshop + "DateCreated")) ?? Date(Value(root, s_exif + "DateTimeOriginal"));
        var latitude = Degrees(Value(root, s_exif + "GPSLatitude"), Value(root, s_exif + "GPSLatitudeRef"), "NS", 90);
        var longitude = Degrees(Value(root, s_exif + "GPSLongitude"), Value(root, s_exif + "GPSLongitudeRef"), "EW", 180);
        return new ExportMetadata
        {
            Caption = caption,
            CapturedAt = date,
            Location = latitude is { } lat && longitude is { } lon ? new GeoPoint(lat, lon) : null,
        };
    }

    /// <summary>
    /// An XMP date as export metadata: <c>Z</c> is UTC; an explicit offset is dropped and the time is kept as written (it is
    /// the wall-clock time where the photo was taken); no zone is wall-clock time. Null for anything else.
    /// </summary>
    public static DateTime? Date(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || Iso().Match(text.Trim()) is not { Success: true } m)
        {
            return null;
        }

        var parts = Enumerable.Range(1, 6).Select(g => m.Groups[g].Success ? int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0).ToArray();
        try
        {
            var kind = string.Equals(m.Groups[7].Value, "Z", StringComparison.OrdinalIgnoreCase) ? DateTimeKind.Utc : DateTimeKind.Unspecified;
            return new DateTime(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], kind);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>A coordinate in the forms XMP uses to a signed decimal number, or null when it is not one or is out of range.</summary>
    public static double? Degrees(string? value, string? reference, string hemispheres, double limit)
    {
        if (string.IsNullOrWhiteSpace(value) || Coordinate().Match(value) is not { Success: true } m)
        {
            return null;
        }

        var numbers = Enumerable.Range(1, 3).Where(g => m.Groups[g].Success)
            .Select(g => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture)).ToList();
        if (numbers.Skip(1).Any(n => n >= 60))
        {
            return null;
        }

        var result = Math.Abs(numbers[0]) + (numbers.Count > 1 ? numbers[1] / 60 : 0) + (numbers.Count > 2 ? numbers[2] / 3600 : 0);
        var signed = m.Groups[1].Value.StartsWith('-');
        var inValue = m.Groups[4].Success ? char.ToUpperInvariant(m.Groups[4].Value[0]) : (char?)null;
        var asReference = !string.IsNullOrWhiteSpace(reference) ? char.ToUpperInvariant(reference.Trim()[0]) : (char?)null;
        if ((inValue is { } v && !hemispheres.Contains(v, StringComparison.Ordinal))
            || (asReference is { } r && !hemispheres.Contains(r, StringComparison.Ordinal))
            || (inValue is { } x && asReference is { } y && x != y))
        {
            return null;
        }

        // A hemisphere letter and a minus sign that disagree would flip the place across the globe: refused, not guessed.
        var letter = inValue ?? asReference;
        if (signed && letter is { } l && l != hemispheres[1])
        {
            return null;
        }

        var negative = signed || letter == hemispheres[1];
        return result <= limit && double.IsFinite(result) ? (negative ? -result : result) : null;
    }

    // An element's text, or the attribute of the same name on any element; the first found, trimmed, null when empty.
    private static string? Value(XElement root, XName name)
    {
        var text = root.Descendants(name).Select(e => e.Value).Concat(root.DescendantsAndSelf().Select(e => (string?)e.Attribute(name)).OfType<string>())
            .Select(v => v.Trim()).FirstOrDefault(v => v.Length > 0);
        return text;
    }

    // A language alternative (rdf:Alt of rdf:li): the x-default entry, else the first with text; or plain text.
    private static string? LanguageText(XElement root, XName name)
    {
        foreach (var property in root.Descendants(name))
        {
            var items = property.Descendants().Where(e => e.Name.LocalName == "li").ToList();
            var candidates = items.Count == 0
                ? [property]
                : items.OrderBy(i => string.Equals((string?)i.Attribute(s_xml + "lang"), "x-default", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
            var text = candidates.Select(c => c.Value.Trim()).FirstOrDefault(v => v.Length > 0);
            if (text is not null)
            {
                return text;
            }
        }

        return root.DescendantsAndSelf().Select(e => (string?)e.Attribute(name)).OfType<string>().Select(v => v.Trim()).FirstOrDefault(v => v.Length > 0);
    }
}
