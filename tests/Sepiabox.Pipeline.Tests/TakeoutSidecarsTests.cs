using Sepiabox.Pipeline.Exports;

namespace Sepiabox.Pipeline.Tests;

/// <summary>Which sidecar belongs to which file in a Google Takeout folder (S-046, D-106), over names only.</summary>
public sealed class TakeoutSidecarsTests
{
    private const string Long = "PXL_20190701_101010123.MP.COVER.ORIGINAL.photo.long.jpg";

    [Theory]
    [InlineData("IMG_0001.jpg", "IMG_0001.jpg.json", "IMG_0001.jpg.json")]
    [InlineData("IMG_0001.jpg", "IMG_0001.jpg.supplemental-metadata.json", "IMG_0001.jpg.supplemental-metadata.json")]
    [InlineData("IMG_0001.JPG", "IMG_0001.jpg.json", "IMG_0001.jpg.json")]
    [InlineData("IMG_0001-edited.jpg", "IMG_0001.jpg.json", "IMG_0001.jpg.json")]
    [InlineData("IMG_0001-bearbeitet.jpg", "IMG_0001.jpg.supplemental-metadata.json", "IMG_0001.jpg.supplemental-metadata.json")]
    [InlineData("IMG_0001-editado.jpg", "IMG_0001.jpg.json", "IMG_0001.jpg.json")]
    [InlineData("IMG_0001(1).jpg", "IMG_0001.jpg(1).json", "IMG_0001.jpg(1).json")]
    [InlineData("IMG_0001(2).jpg", "IMG_0001.jpg.supplemental-metadata(2).json", "IMG_0001.jpg.supplemental-metadata(2).json")]
    public void Finds_the_sidecar_for_each_naming_quirk(string media, string sidecar, string expected)
    {
        var match = TakeoutSidecars.Match([media], [sidecar, "metadata.json", "other.jpg.json"]);

        Assert.Equal(expected, match[media]);
    }

    [Fact]
    public void A_duplicate_gets_its_own_sidecar_and_never_the_first_copys()
    {
        var match = TakeoutSidecars.Match(
            ["IMG_0001.jpg", "IMG_0001(1).jpg"],
            ["IMG_0001.jpg.json", "IMG_0001.jpg(1).json"]);

        Assert.Equal("IMG_0001.jpg.json", match["IMG_0001.jpg"]);
        Assert.Equal("IMG_0001.jpg(1).json", match["IMG_0001(1).jpg"]);
        Assert.Null(TakeoutSidecars.Match(["IMG_0001(1).jpg"], ["IMG_0001.jpg.json"])["IMG_0001(1).jpg"]);
    }

    [Fact]
    public void An_edited_copy_shares_the_originals_sidecar_with_it()
    {
        var match = TakeoutSidecars.Match(["IMG_0001.jpg", "IMG_0001-edited.jpg"], ["IMG_0001.jpg.json"]);

        Assert.Equal("IMG_0001.jpg.json", match["IMG_0001.jpg"]);
        Assert.Equal("IMG_0001.jpg.json", match["IMG_0001-edited.jpg"]);
    }

    [Fact]
    public void An_edited_copy_with_a_sidecar_of_its_own_uses_it()
    {
        var match = TakeoutSidecars.Match(["IMG_0001-edited.jpg"], ["IMG_0001-edited.jpg.json", "IMG_0001.jpg.json"]);

        Assert.Equal("IMG_0001-edited.jpg.json", match["IMG_0001-edited.jpg"]);
    }

    [Fact]
    public void Finds_a_sidecar_whose_name_was_cut_at_the_length_limit()
    {
        var cutSupplemental = (Long + TakeoutSidecars.Supplemental)[..46] + ".json";
        var cutPlain = Long[..46] + ".json";

        Assert.Equal(cutSupplemental, TakeoutSidecars.Match([Long], [cutSupplemental, "metadata.json"])[Long]);
        Assert.Equal(cutPlain, TakeoutSidecars.Match([Long], [cutPlain])[Long]);
    }

    [Fact]
    public void Leaves_a_cut_sidecar_unmatched_when_two_files_could_own_it()
    {
        var one = "PXL_20190701_101010123.MP.COVER.ORIGINAL.photo_1.jpg";
        var two = "PXL_20190701_101010123.MP.COVER.ORIGINAL.photo_2.jpg";
        var cut = (one + TakeoutSidecars.Supplemental)[..46] + ".json";

        var match = TakeoutSidecars.Match([one, two], [cut]);

        Assert.Null(match[one]);
        Assert.Null(match[two]);
    }

    [Fact]
    public void An_original_with_a_long_name_and_its_edited_copy_share_the_cut_sidecar()
    {
        var original = Long;
        var edited = Long.Replace(".jpg", "-edited.jpg", StringComparison.Ordinal);
        var cut = (Long + TakeoutSidecars.Supplemental)[..46] + ".json";

        var match = TakeoutSidecars.Match([original, edited], [cut]);

        Assert.Equal(cut, match[original]);
        Assert.Equal(cut, match[edited]);
        // An edited copy alone, whose original is not in the folder, finds it too.
        Assert.Equal(cut, TakeoutSidecars.Match([edited], [cut])[edited]);
    }

    [Fact]
    public void A_cut_sidecar_is_still_not_shared_with_a_different_photo_that_could_own_it()
    {
        var one = "PXL_20190701_101010123.MP.COVER.ORIGINAL.photo_1.jpg";
        var two = "PXL_20190701_101010123.MP.COVER.ORIGINAL.photo_2-edited.jpg";
        var cut = (one + TakeoutSidecars.Supplemental)[..46] + ".json";

        var match = TakeoutSidecars.Match([one, two], [cut]);

        Assert.Null(match[one]);
        Assert.Null(match[two]);
    }

    [Theory]
    [InlineData("IMG-edited(1).jpg", "IMG(1).jpg")]
    [InlineData("IMG-edited.jpg", "IMG.jpg")]
    [InlineData("IMG-modifie\u0301.jpg", "IMG.jpg")]
    [InlineData("IMG-modifi\u00e9.jpg", "IMG.jpg")]
    [InlineData("IMG(1).jpg", "IMG(1).jpg")]
    public void Finds_the_original_of_an_edited_copy_in_normalization_form_c_and_keeps_a_duplicates_number(string media, string expected)
    {
        Assert.Equal(expected, TakeoutSidecars.Original(media));
    }

    [Fact]
    public void A_second_edited_copy_uses_the_duplicates_sidecar_and_names_are_compared_in_form_c()
    {
        var match = TakeoutSidecars.Match(["IMG-edited(1).jpg", "Fe\u0301rias-edited.jpg"], ["IMG.jpg(1).json", "F\u00e9rias.jpg.json"]);

        Assert.Equal("IMG.jpg(1).json", match["IMG-edited(1).jpg"]);
        Assert.Equal("F\u00e9rias.jpg.json", match["Fe\u0301rias-edited.jpg"]);
    }

    [Fact]
    public void Prefers_an_exact_sidecar_to_a_cut_one_and_does_not_give_one_files_sidecar_to_another()
    {
        // Different at the start, so the cut name still tells them apart.
        var one = "a_" + Long;
        var two = "b_" + Long;
        var exactOne = one + ".json";
        var cutTwo = (two + TakeoutSidecars.Supplemental)[..46] + ".json";

        var match = TakeoutSidecars.Match([one, two], [exactOne, cutTwo]);

        Assert.Equal(exactOne, match[one]);
        Assert.Equal(cutTwo, match[two]);
    }

    [Fact]
    public void Gives_a_file_without_a_sidecar_null_and_does_not_take_the_albums_metadata_for_one()
    {
        var match = TakeoutSidecars.Match(["IMG_0001.jpg", "IMG_0002.jpg"], ["IMG_0002.jpg.json", "metadata.json"]);

        Assert.Null(match["IMG_0001.jpg"]);
        Assert.Equal("IMG_0002.jpg.json", match["IMG_0002.jpg"]);
    }

    [Fact]
    public void Lists_the_stems_a_file_may_have_most_specific_first()
    {
        Assert.Equal(
            ["IMG.jpg(1)", "IMG.jpg.supplemental-metadata(1)", "IMG(1).jpg", "IMG(1).jpg.supplemental-metadata"],
            TakeoutSidecars.Stems("IMG(1).jpg"));
        Assert.Equal("IMG.jpg", TakeoutSidecars.Original("IMG-edited.jpg"));
        Assert.Equal("IMG-edited-twice.jpg", TakeoutSidecars.Original("IMG-edited-twice.jpg"));
        Assert.Equal("-edited.jpg", TakeoutSidecars.Original("-edited.jpg"));
    }

    [Fact]
    public void Gives_no_sidecar_when_two_cut_sidecars_both_start_the_media_name()
    {
        // Taking the first would put one photo's date, place and album on a photo that has none of its own.
        var twoPlain = TakeoutSidecars.Match([Long], [Long[..42] + ".json", Long[..46] + ".json"]);
        var twoSupplemental = TakeoutSidecars.Match([Long], [(Long + TakeoutSidecars.Supplemental)[..44] + ".json", (Long + TakeoutSidecars.Supplemental)[..46] + ".json"]);

        Assert.Null(twoPlain[Long]);
        Assert.Null(twoSupplemental[Long]);
    }

    [Theory]
    [InlineData(39, false)]
    [InlineData(40, true)]
    [InlineData(46, true)]
    public void Accepts_a_cut_name_only_from_the_minimum_length(int length, bool matched)
    {
        var cut = Long[..length] + ".json";

        var found = TakeoutSidecars.Match([Long], [cut])[Long];

        Assert.Equal(matched ? cut : null, found);
    }

    [Fact]
    public void Does_not_match_a_cut_name_that_is_as_long_as_the_media_name_or_longer()
    {
        // The cut rule needs a name strictly shorter than the media's: a name as long, but different, is another file's.
        var sameLength = Long[..^1] + "X.json";
        var longer = Long + ".extra.json";

        Assert.Null(TakeoutSidecars.Match([Long], [sameLength])[Long]);
        Assert.Null(TakeoutSidecars.Match([Long], [longer])[Long]);
    }

    [Fact]
    public void Does_not_offer_a_sidecar_that_another_file_already_has_to_a_longer_name_it_starts()
    {
        var other = Long + ".extra.jpg";

        var match = TakeoutSidecars.Match([Long, other], [Long + ".json"]);

        Assert.Equal(Long + ".json", match[Long]);
        Assert.Null(match[other]);
    }

    [Theory]
    [InlineData("IMG_0001-EDITED.jpg")]
    [InlineData("IMG_0001-Bearbeitet.jpg")]
    [InlineData("IMG_0001-\u7de8\u96c6\u6e08\u307f.jpg")]
    [InlineData("IMG_0001-\ud3b8\uc9d1\ub428.jpg")]
    public void Knows_the_edited_word_whatever_its_case_and_the_last_languages_too(string media)
    {
        Assert.Equal("IMG_0001.jpg.json", TakeoutSidecars.Match([media], ["IMG_0001.jpg.json"])[media]);
    }

    [Fact]
    public void Matches_names_in_either_normalization_form_for_the_media_and_for_the_sidecar()
    {
        var nfdMedia = "Fe\u0301rias.jpg";
        var nfcMedia = "F\u00e9rias.jpg";

        Assert.Equal("F\u00e9rias.jpg.json", TakeoutSidecars.Match([nfdMedia], ["F\u00e9rias.jpg.json"])[nfdMedia]);
        Assert.Equal("Fe\u0301rias.jpg.json", TakeoutSidecars.Match([nfcMedia], ["Fe\u0301rias.jpg.json"])[nfcMedia]);
    }

    [Fact]
    public void Reads_a_sidecar_whose_extension_is_in_capitals()
    {
        Assert.Equal("IMG_0001.jpg.JSON", TakeoutSidecars.Match(["IMG_0001.jpg"], ["IMG_0001.jpg.JSON"])["IMG_0001.jpg"]);
    }
}
