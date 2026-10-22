using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Exports;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

/// <summary>What an export adds to a manifest (S-045, D-104): dates, places, captions and albums, and how they merge.</summary>
public sealed class ExportMetadataTests
{
    private static readonly TimeZoneInfo s_lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    private static readonly TimeProvider s_clock = new FixedClock(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_utc_time_becomes_wall_clock_time_in_the_zone_and_any_other_kind_is_used_as_it_is()
    {
        var utc = new ExportMetadata { CapturedAt = new DateTime(2015, 6, 1, 10, 0, 0, DateTimeKind.Utc) };
        var wall = new ExportMetadata { CapturedAt = new DateTime(2015, 6, 1, 10, 0, 0, DateTimeKind.Unspecified) };

        Assert.Equal(new DateTime(2015, 6, 1, 11, 0, 0), ExportFacts.WallClock(utc, s_lisbon));
        Assert.Equal(new DateTime(2015, 12, 1, 10, 0, 0), ExportFacts.WallClock(utc with { CapturedAt = new DateTime(2015, 12, 1, 10, 0, 0, DateTimeKind.Utc) }, s_lisbon));
        Assert.Equal(new DateTime(2015, 6, 1, 10, 0, 0), ExportFacts.WallClock(wall, s_lisbon));
        Assert.Equal(DateTimeKind.Unspecified, ExportFacts.WallClock(utc, s_lisbon)!.Value.Kind);
        Assert.Null(ExportFacts.WallClock(new ExportMetadata(), s_lisbon));
        Assert.Null(ExportFacts.WallClock(null, s_lisbon));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(91, 10, false)]
    [InlineData(10, -181, false)]
    [InlineData(double.NaN, 10, false)]
    [InlineData(0, 10, true)]
    [InlineData(41.1579, -8.6291, true)]
    [InlineData(-90, 180, true)]
    public void Uses_a_location_only_when_it_is_on_the_globe_and_not_zero_zero(double latitude, double longitude, bool usable)
    {
        var location = ExportFacts.Location(new ExportMetadata { Location = new GeoPoint(latitude, longitude) });

        Assert.Equal(usable, location is not null);
    }

    [Fact]
    public void Trims_a_caption_cuts_it_at_2000_characters_and_drops_an_empty_one()
    {
        Assert.Equal("Praia", ExportFacts.Caption(new ExportMetadata { Caption = "  Praia\n" }));
        Assert.Null(ExportFacts.Caption(new ExportMetadata { Caption = "  \t " }));
        Assert.Null(ExportFacts.Caption(new ExportMetadata()));
        Assert.Equal(2000, ExportFacts.Caption(new ExportMetadata { Caption = new string('a', 2500) })!.Length);
        // A pair of surrogates is never cut in half.
        var cut = ExportFacts.Caption(new ExportMetadata { Caption = new string('a', 1999) + "\U0001F600" })!;
        Assert.Equal(1999, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));
    }

    [Fact]
    public void Puts_captions_and_album_names_in_unicode_normalization_form_c_so_one_name_is_one_album()
    {
        var decomposed = "Cafe\u0301 da Praia";
        var composed = "Caf\u00e9 da Praia";

        Assert.Equal(composed, ExportFacts.Caption(new ExportMetadata { Caption = decomposed }));
        Assert.Equal([composed], ExportFacts.Albums(new ExportMetadata { Albums = [decomposed, composed] }));
        Assert.Equal([composed], ExportFacts.Albums(new ExportMetadata { Albums = ["Cafe\u0301 da Praia"] }));
    }

    [Fact]
    public void Finds_a_file_whose_name_is_written_in_the_other_normalization_form()
    {
        var scan = new ExportScan([new KeyValuePair<string, ExportMetadata>("F\u00e9rias/a.jpg", new ExportMetadata { Caption = "x" })]);

        Assert.Equal("x", scan.Find("Fe\u0301rias/a.jpg")!.Caption);
    }

    [Fact]
    public void Uses_a_local_kind_time_as_wall_clock_time_without_converting_it()
    {
        var local = new ExportMetadata { CapturedAt = new DateTime(2015, 6, 1, 10, 0, 0, DateTimeKind.Local) };

        Assert.Equal(new DateTime(2015, 6, 1, 10, 0, 0), ExportFacts.WallClock(local, s_lisbon));
        Assert.Equal(DateTimeKind.Unspecified, ExportFacts.WallClock(local, s_lisbon)!.Value.Kind);
    }

    [Fact]
    public void Does_not_leave_a_space_at_the_end_of_a_caption_it_cut()
    {
        // The cut falls on a space: 1,999 letters, a space, then more.
        var caption = ExportFacts.Caption(new ExportMetadata { Caption = new string('a', 1999) + " " + new string('b', 10) })!;

        Assert.Equal(1999, caption.Length);
        Assert.False(char.IsWhiteSpace(caption[^1]));
    }

    [Theory]
    [InlineData("/Photos/a.jpg")]
    [InlineData("//Photos/a.jpg")]
    [InlineData(@"\Photos\a.jpg")]
    public void Finds_a_file_whatever_leading_slashes_the_path_has(string asked)
    {
        var scan = new ExportScan([new KeyValuePair<string, ExportMetadata>("Photos/a.jpg", new ExportMetadata { Caption = "x" })]);

        Assert.Equal("x", scan.Find(asked)?.Caption);
        Assert.True(new ExportScan(leftOut: ["Photos/a.jpg"]).IsLeftOut(asked));
    }

    [Fact]
    public void Cleans_albums_to_distinct_trimmed_names_in_ordinal_order()
    {
        Assert.Equal(["Ferias", "Viagem"], ExportFacts.Albums(new ExportMetadata { Albums = [" Viagem", "Ferias", "Viagem", "", "  "] }));
        Assert.Null(ExportFacts.Albums(new ExportMetadata { Albums = ["", " "] }));
        Assert.Null(ExportFacts.Albums(null));
        Assert.Equal(["B", "a"], ExportFacts.Clean(["a", "B"]));
    }

    [Fact]
    public void The_export_date_comes_after_the_files_own_and_before_the_name_and_the_folder()
    {
        var export = new DateTime(2015, 6, 1, 11, 0, 0);
        var exif = new DateTime(2012, 3, 4, 10, 11, 12);

        var own = CaptureDate.Resolve(exif, "2005-07-14/IMG_20120304_101112.jpg", s_clock, export);
        var viaExport = CaptureDate.Resolve(null, "2005-07-14/IMG_20120304_101112.jpg", s_clock, export);
        var viaName = CaptureDate.Resolve(null, "2005-07-14/IMG_20120304_101112.jpg", s_clock);
        var viaFolder = CaptureDate.Resolve(null, "2005-07-14/a.jpg", s_clock);

        Assert.Equal((exif, CaptureDateSource.Metadata), (own!.Value, own.Source));
        Assert.Equal((export, CaptureDateSource.Export), (viaExport!.Value, viaExport.Source));
        Assert.Equal(CaptureDateSource.FileName, viaName!.Source);
        Assert.Equal(CaptureDateSource.Folder, viaFolder!.Source);
        Assert.Equal(CaptureDateSource.Export, CaptureDate.Resolve(null, "2005-07-14/a.jpg", s_clock, export)!.Source);
    }

    [Theory]
    [InlineData("1980-01-01T00:00:00")]
    [InlineData("2099-01-01T00:00:00")]
    public void Ignores_an_export_date_that_is_not_plausible(string value)
    {
        var date = CaptureDate.Resolve(null, "2005-07-14/a.jpg", s_clock, DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(CaptureDateSource.Folder, date!.Source);
    }

    [Fact]
    public void Ranks_metadata_over_export_over_file_name_over_folder_in_either_order()
    {
        CaptureDateSource[] strongestFirst = [CaptureDateSource.Metadata, CaptureDateSource.Export, CaptureDateSource.FileName, CaptureDateSource.Folder];
        for (var stronger = 0; stronger < strongestFirst.Length; stronger++)
        {
            for (var weaker = stronger + 1; weaker < strongestFirst.Length; weaker++)
            {
                // The weaker one has the earlier date, so the date cannot be what decides.
                var strong = new CaptureDate(new DateTime(2016, 1, 1), strongestFirst[stronger]);
                var weak = new CaptureDate(new DateTime(2010, 1, 1), strongestFirst[weaker]);

                Assert.Equal(strong, CaptureDate.Stronger(strong, weak));
                Assert.Equal(strong, CaptureDate.Stronger(weak, strong));
            }
        }
    }

    [Fact]
    public void Between_two_export_dates_the_earlier_wins_whatever_the_order()
    {
        var early = new CaptureDate(new DateTime(2015, 6, 1), CaptureDateSource.Export);
        var late = new CaptureDate(new DateTime(2016, 6, 1), CaptureDateSource.Export);

        Assert.Equal(early, CaptureDate.Stronger(early, late));
        Assert.Equal(early, CaptureDate.Stronger(late, early));
    }

    [Fact]
    public void Writes_the_export_source_as_a_word_and_reads_it_back()
    {
        var manifest = Manifest("a") with { Captured = new CaptureDate(new DateTime(2015, 6, 1, 11, 0, 0), CaptureDateSource.Export) };

        var json = ManifestJson.Serialize(manifest);

        Assert.Equal("export", (string?)JsonNode.Parse(json)!["capturedAtSource"]);
        Assert.Equal(CaptureDateSource.Export, ManifestJson.Deserialize(json).CapturedAtSource);
    }

    [Fact]
    public void Merges_albums_as_a_union_and_captions_as_the_longer_then_the_greater_in_either_order()
    {
        var a = Manifest("a") with { Caption = "Praia", Albums = ["Viagem", "Ferias"] };
        var b = Manifest("a") with { Caption = "Praia de Matosinhos", Albums = ["Ferias", "2015"] };

        var ab = ManifestJson.Serialize(ManifestMerge.Merge(a, b));
        var ba = ManifestJson.Serialize(ManifestMerge.Merge(b, a));

        Assert.Equal(ab, ba);
        var merged = ManifestJson.Deserialize(ab);
        Assert.Equal("Praia de Matosinhos", merged.Caption);
        Assert.Equal(["2015", "Ferias", "Viagem"], merged.Albums);
    }

    [Fact]
    public void Breaks_a_tie_between_captions_of_the_same_length_by_the_ordinally_greater()
    {
        Assert.Equal("beta", ManifestMerge.LongerCaption("alfa", "beta"));
        Assert.Equal("beta", ManifestMerge.LongerCaption("beta", "alfa"));
        Assert.Equal("a", ManifestMerge.LongerCaption("a", null));
        Assert.Equal("a", ManifestMerge.LongerCaption(null, "a"));
        Assert.Null(ManifestMerge.LongerCaption(null, null));
        // Ordinal, not by culture: "B" (66) is before "a" (97).
        Assert.Equal("a", ManifestMerge.LongerCaption("B", "a"));
    }

    [Fact]
    public void Merging_three_manifests_gives_the_same_blob_in_every_order_and_grouping()
    {
        var manifests = new[]
        {
            Manifest("a") with { Caption = "um", Albums = ["B"], Captured = new CaptureDate(new DateTime(2015, 1, 1), CaptureDateSource.Folder) },
            Manifest("a") with { Caption = "dois!", Albums = ["A", "B"], Captured = new CaptureDate(new DateTime(2016, 2, 2), CaptureDateSource.Export) },
            Manifest("a") with { Caption = "tres!", Albums = ["C"], Captured = new CaptureDate(new DateTime(2015, 6, 6), CaptureDateSource.Export) },
        };
        var results = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in Permutations([0, 1, 2]))
        {
            var (x, y, z) = (manifests[order[0]], manifests[order[1]], manifests[order[2]]);
            results.Add(ManifestJson.Serialize(ManifestMerge.Merge(ManifestMerge.Merge(x, y), z)));
            results.Add(ManifestJson.Serialize(ManifestMerge.Merge(x, ManifestMerge.Merge(y, z))));
        }

        var only = ManifestJson.Deserialize(Assert.Single(results));
        Assert.Equal(("tres!", new DateTime(2015, 6, 6), CaptureDateSource.Export), (only.Caption, only.CapturedAt, only.CapturedAtSource));
        Assert.Equal(["A", "B", "C"], only.Albums);
    }

    [Fact]
    public void Leaves_a_manifest_without_the_new_fields_as_it_was()
    {
        var merged = ManifestMerge.Merge(Manifest("a"), Manifest("a"));
        var json = JsonNode.Parse(ManifestJson.Serialize(merged))!.AsObject();

        Assert.False(json.ContainsKey("caption"));
        Assert.False(json.ContainsKey("albums"));
    }

    [Fact]
    public void The_schema_accepts_a_caption_and_albums_and_a_manifest_without_them()
    {
        var validator = new ManifestValidator();

        Assert.True(validator.TryValidate(ManifestJson.Serialize(Manifest("a")), out _, out var plain), plain);
        var full = ManifestJson.Serialize(Manifest("a") with
        {
            Caption = "Praia",
            Albums = ["Ferias", "Viagem"],
            Captured = new CaptureDate(new DateTime(2015, 6, 1, 11, 0, 0), CaptureDateSource.Export),
        });
        Assert.True(validator.TryValidate(full, out var manifest, out var error), error);
        Assert.Equal(("Praia", 2), (manifest.Caption, manifest.Albums!.Count));
    }

    [Theory]
    [InlineData("caption", "")]
    [InlineData("caption", "TOO_LONG")]
    [InlineData("caption", 7)]
    [InlineData("albums", "Viagem")]
    public void The_schema_refuses_a_malformed_caption_or_albums(string property, object value)
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(Manifest("a")))!.AsObject();
        json[property] = value is "TOO_LONG" ? new string('a', 2001) : JsonValue.Create(value);

        Assert.False(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out _));
    }

    [Theory]
    [InlineData("""["a","a"]""")]
    [InlineData("""["a",""]""")]
    [InlineData("""["a",1]""")]
    public void The_schema_refuses_albums_that_repeat_or_are_empty_or_not_text(string albums)
    {
        var json = JsonNode.Parse(ManifestJson.Serialize(Manifest("a")))!.AsObject();
        json["albums"] = JsonNode.Parse(albums);

        Assert.False(new ManifestValidator().TryValidate(json.ToJsonString(), out _, out _));
    }

    [Fact]
    public void Finds_a_file_in_a_scan_whatever_its_slashes_and_case_and_knows_what_is_left_out()
    {
        var scan = new ExportScan(
            [new KeyValuePair<string, ExportMetadata>(@"Photos\2015\A.JPG", new ExportMetadata { Caption = "x" })],
            ["thumbs/small.jpg"]);

        Assert.Equal("x", scan.Find("photos/2015/a.jpg")!.Caption);
        Assert.Null(scan.Find("photos/2015/b.jpg"));
        Assert.True(scan.IsLeftOut(@"Thumbs\Small.jpg"));
        Assert.False(scan.IsLeftOut("photos/2015/a.jpg"));
        Assert.Equal(1, scan.Count);
    }

    [Fact]
    public void Lists_the_known_kinds_in_order_and_finds_a_reader_by_kind()
    {
        var registry = new ExportReaderRegistry([new NamedReader("instagram"), new NamedReader("apple-photos")]);

        Assert.Equal(["apple-photos", "instagram"], registry.Kinds);
        Assert.True(registry.TryGet("instagram", out var reader));
        Assert.Equal("instagram", reader.Kind);
        Assert.False(registry.TryGet("Instagram", out _));
        Assert.Equal(["apple-photos", "google-takeout", "instagram"], ExportReaderRegistry.Default.Kinds);
    }

    private static Manifest Manifest(string id) => TestManifests.Photo(TestManifests.Source("HD da Ana", $"{id}.jpg")) with { Captured = null };

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length == 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            foreach (var rest in Permutations([.. items.Where((_, index) => index != i)]))
            {
                yield return [items[i], .. rest];
            }
        }
    }

    private sealed class NamedReader(string kind) : IExportReader
    {
        public string Kind => kind;

        public ExportScan Read(string root, CancellationToken cancellationToken = default) => new();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
