using Sepiabox.Pipeline.Exports;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

/// <summary>The XMP sidecars of the Photos app (S-047, D-109), written here by hand: no real person's data.</summary>
public sealed class AppleXmpTests
{
    private static string Xmp(string properties, string attributes = "") => $"""
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
                xmlns:dc="http://purl.org/dc/elements/1.1/"
                xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/"
                xmlns:exif="http://ns.adobe.com/exif/1.0/" {attributes}>
              {properties}
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;

    [Fact]
    public void Reads_the_description_the_date_and_the_position()
    {
        var facts = AppleXmp.Parse(Xmp("""
            <dc:description><rdf:Alt><rdf:li xml:lang="x-default">Beach day</rdf:li></rdf:Alt></dc:description>
            <dc:subject><rdf:Bag><rdf:li>keyword</rdf:li></rdf:Bag></dc:subject>
            <photoshop:DateCreated>2018-05-23T16:16:45</photoshop:DateCreated>
            <exif:GPSLatitude>38,43.338N</exif:GPSLatitude>
            <exif:GPSLongitude>9,8.358W</exif:GPSLongitude>
            """))!;

        Assert.Equal("Beach day", facts.Caption);
        Assert.Equal(new DateTime(2018, 5, 23, 16, 16, 45, DateTimeKind.Unspecified), facts.CapturedAt);
        Assert.Equal(38.7223, facts.Location!.Latitude, 4);
        Assert.Equal(-9.1393, facts.Location.Longitude, 4);
    }

    [Fact]
    public void Uses_the_title_when_there_is_no_description_and_prefers_the_default_language()
    {
        var titled = AppleXmp.Parse(Xmp("""<dc:title><rdf:Alt><rdf:li xml:lang="en">Sunset</rdf:li></rdf:Alt></dc:title>"""))!;
        var both = AppleXmp.Parse(Xmp("""
            <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Title</rdf:li></rdf:Alt></dc:title>
            <dc:description><rdf:Alt><rdf:li xml:lang="pt">Pôr do sol</rdf:li><rdf:li xml:lang="x-default">Sunset glow</rdf:li></rdf:Alt></dc:description>
            """))!;

        Assert.Equal("Sunset", titled.Caption);
        Assert.Equal("Sunset glow", both.Caption);
    }

    [Fact]
    public void Reads_properties_written_as_attributes_and_falls_back_to_the_exif_date()
    {
        var facts = AppleXmp.Parse(Xmp(string.Empty, """exif:DateTimeOriginal="2018-05-23T16:16:45+01:00" exif:GPSLatitude="38.7223" exif:GPSLatitudeRef="N" exif:GPSLongitude="9.1393" exif:GPSLongitudeRef="W" """))!;

        Assert.Equal(new DateTime(2018, 5, 23, 16, 16, 45), facts.CapturedAt);
        Assert.Equal(DateTimeKind.Unspecified, facts.CapturedAt!.Value.Kind);
        Assert.Equal(new GeoPoint(38.7223, -9.1393), facts.Location);
    }

    [Theory]
    [InlineData("2018-05-23T16:16:45Z", "2018-05-23T16:16:45", DateTimeKind.Utc)]
    [InlineData("2018-05-23T16:16:45.123", "2018-05-23T16:16:45", DateTimeKind.Unspecified)]
    [InlineData("2018-05-23T16:16", "2018-05-23T16:16:00", DateTimeKind.Unspecified)]
    [InlineData("2018-05-23", "2018-05-23T00:00:00", DateTimeKind.Unspecified)]
    [InlineData("2018-05-23T16:16:45-0300", "2018-05-23T16:16:45", DateTimeKind.Unspecified)]
    public void Reads_the_forms_of_an_xmp_date(string text, string expected, DateTimeKind kind)
    {
        var date = AppleXmp.Date(text);

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), date);
        Assert.Equal(kind, date!.Value.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("2018-13-45T10:00:00")]
    [InlineData("2018:05:23 16:16:45")]
    public void Reads_no_date_from_anything_else(string text) => Assert.Null(AppleXmp.Date(text));

    [Theory]
    [InlineData("38,43.338N", null, "NS", 38.7223)]
    [InlineData("38,43,20.28N", null, "NS", 38.7223)]
    [InlineData("38,43.338S", null, "NS", -38.7223)]
    [InlineData("9,8.358W", null, "EW", -9.1393)]
    [InlineData("-9.1393", null, "EW", -9.1393)]
    [InlineData("9.1393", "W", "EW", -9.1393)]
    [InlineData("9.1393", "E", "EW", 9.1393)]
    [InlineData("41", null, "NS", 41.0)]
    public void Reads_the_coordinate_forms(string value, string? reference, string hemispheres, double expected) =>
        Assert.Equal(expected, AppleXmp.Degrees(value, reference, hemispheres, 180)!.Value, 3);

    [Theory]
    [InlineData("91.0", null, "NS")]
    [InlineData("38,75.0N", null, "NS")]
    [InlineData("38E", null, "NS")]
    [InlineData("9.1393", "X", "EW")]
    [InlineData("north", null, "NS")]
    [InlineData("", null, "NS")]
    public void Reads_no_coordinate_when_it_is_out_of_range_or_not_one(string value, string? reference, string hemispheres) =>
        Assert.Null(AppleXmp.Degrees(value, reference, hemispheres, 90));

    [Fact]
    public void Gives_a_position_only_when_both_coordinates_are_there()
    {
        var facts = AppleXmp.Parse(Xmp("<exif:GPSLatitude>38,43.338N</exif:GPSLatitude>"))!;

        Assert.Null(facts.Location);
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<a><b></a>")]
    [InlineData("")]
    public void Returns_null_for_text_that_is_not_xml(string text) => Assert.Null(AppleXmp.Parse(text));

    [Fact]
    public void Refuses_a_dtd_instead_of_fetching_or_expanding_it()
    {
        var text = """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/hostname">]><x:xmpmeta xmlns:x="adobe:ns:meta/">&e;</x:xmpmeta>""";

        Assert.Null(AppleXmp.Parse(text));
    }

    [Theory]
    [InlineData("90", null, "NS", 90.0)]
    [InlineData("-90", null, "NS", -90.0)]
    [InlineData("-0.5", null, "NS", -0.5)]
    [InlineData("-9.1", "W", "EW", -9.1)]
    [InlineData("9.1W", "W", "EW", -9.1)]
    public void Accepts_the_edges_of_the_globe_and_a_sign_that_agrees_with_the_letter(string value, string? reference, string hemispheres, double expected) =>
        Assert.Equal(expected, AppleXmp.Degrees(value, reference, hemispheres, hemispheres == "NS" ? 90 : 180)!.Value, 3);

    [Theory]
    [InlineData("38,60.0N", null, "NS")]
    [InlineData("38,59,60N", null, "NS")]
    [InlineData("-9.1", "N", "NS")]
    [InlineData("-9.1", "E", "EW")]
    [InlineData("9.1W", "E", "EW")]
    [InlineData("9.1E", "W", "EW")]
    [InlineData("90.1", null, "NS")]
    public void Refuses_minutes_of_sixty_a_sign_against_the_letter_and_letters_that_disagree(string value, string? reference, string hemispheres) =>
        Assert.Null(AppleXmp.Degrees(value, reference, hemispheres, hemispheres == "NS" ? 90 : 180));

    [Fact]
    public void Takes_the_photoshop_date_when_the_exif_date_differs()
    {
        var facts = AppleXmp.Parse(Xmp("""
            <exif:DateTimeOriginal>2016-05-06T07:08:09</exif:DateTimeOriginal>
            <photoshop:DateCreated>2017-01-02T03:04:05</photoshop:DateCreated>
            """))!;

        Assert.Equal(new DateTime(2017, 1, 2, 3, 4, 5), facts.CapturedAt);
    }
}
