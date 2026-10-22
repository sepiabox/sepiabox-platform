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
}
