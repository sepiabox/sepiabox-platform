using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Clip;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>"Photos like this part of a photo" (S-041, D-099) without a database: the filter, its links and the page.</summary>
public sealed class RegionSearchTests : LocalizedContext
{
    private static readonly string s_id = new('a', 64);

    private static string WithId(string json) => json.Replace("ID", s_id, StringComparison.Ordinal);

    [Fact]
    public void Parses_a_region_and_keeps_it_through_json_and_the_address()
    {
        Assert.True(SearchQueryJson.TryParse($$$$"""{"filters": {"similarToRegion": {"assetId": "{{{{s_id}}}}", "box": [0.1, 0.2, 0.3, 0.4]}, "years": [2020]}}""", out var query, out var error), error);

        var region = query.Filters.SimilarToRegion!;
        Assert.Equal(s_id, region.AssetId);
        Assert.Equal([0.1, 0.2, 0.3, 0.4], region.Box);
        Assert.Null(region.Limit);
        Assert.Equal(SimilarToFilter.DefaultLimit, query.ResultLimit);
        Assert.True(SearchQueryJson.TryDecode(SearchQueryJson.Encode(query), out var decoded, out _));
        Assert.Equal(query, decoded);
        Assert.NotEqual(query, query with { Filters = query.Filters with { SimilarToRegion = region with { Box = [0.1, 0.2, 0.3, 0.5] } } });
    }

    [Fact]
    public void The_limit_is_capped_by_the_query_s_own()
    {
        Assert.True(SearchQueryJson.TryParse($$$$"""{"filters": {"similarToRegion": {"assetId": "{{{{s_id}}}}", "box": [0, 0, 1, 1], "limit": 500}}, "limit": 20}""", out var query, out _));

        Assert.Equal(20, query.ResultLimit);
    }

    [Theory]
    [InlineData("""[0.1, 0.2, 0.3]""", "box")]
    [InlineData("""[0.1, 0.2, 0.3, 0.4, 0.5]""", "box")]
    [InlineData("""[-0.1, 0.2, 0.3, 0.4]""", "box")]
    [InlineData("""[1.5, 0.2, 0.3, 0.4]""", "box")]
    [InlineData("""[0.1, 0.2, 0, 0.4]""", "box")]
    [InlineData("""[0.1, 0.2, 0.3, 0]""", "box")]
    [InlineData("""[0.8, 0.2, 0.3, 0.4]""", "box")]
    [InlineData("""[0.1, 0.8, 0.3, 0.4]""", "box")]
    [InlineData("""[0.1, 0.2, 0.3, 0.4], "limit": 0""", "limit")]
    [InlineData("""[0.1, 0.2, 0.3, 0.4], "limit": 1001""", "limit")]
    [InlineData("""[0.1, 0.2, 0.3, 0.4], "minScore": 0.5""", "minScore")]
    public void Refuses_a_region_that_is_not_a_box_inside_the_photo(string box, string expected)
    {
        var ok = SearchQueryJson.TryParse(WithId("""{"filters": {"similarToRegion": {"assetId": "ID", "box": """ + box + "}}}"), out _, out var error);

        Assert.False(ok);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    // One table for both places that decide whether a box is inside the photo: the filter and the crop.
    public static TheoryData<double[], bool> Boxes => new()
    {
        { [0, 0, 1, 1], true },
        { [0.34, 0, 0.5, 1], true },
        { [0.5, 0.5, 0.5 + 1e-10, 0.5], true },
        { [0.5, 0, 0.5 + 1e-9, 1], true },
        { [0.5, 0, 0.5 + 1e-8, 1], false },
        { [0, 0.5, 1, 0.5 + 1e-8], false },
        { [0, 0, 0, 1], false },
        { [0, 0, 1, 0], false },
        { [-1e-12, 0, 1, 1], false },
        { [0.1, 0.2, 0.3], false },
        { [double.NaN, 0, 1, 1], false },
        { [0, 0, double.PositiveInfinity, 1], false },
    };

    [Theory]
    [MemberData(nameof(Boxes))]
    public void The_filter_and_the_crop_agree_on_which_boxes_are_inside_the_photo(double[] box, bool valid)
    {
        Assert.Equal(valid, SimilarToRegionFilter.IsValidBox(box));
        Assert.Equal(valid, RegionCrop.IsValid(box));
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(63, false)]
    [InlineData(65, false)]
    public void Wants_an_asset_id_of_exactly_64_characters(int length, bool valid)
    {
        var id = new string('a', length);

        var ok = SearchQueryJson.TryParse("""{"filters": {"similarToRegion": {"assetId": "ID", "box": [0, 0, 1, 1]}}}""".Replace("ID", id, StringComparison.Ordinal), out _, out var error);

        Assert.Equal(valid, ok);
        Assert.True(valid || error!.Contains("assetId", StringComparison.Ordinal));
    }

    [Fact]
    public void Wants_the_asset_id_in_lower_case_hexadecimal()
    {
        var upper = new string('A', 64);
        var notHex = new string('g', 64);

        Assert.False(SearchQueryJson.TryParse("""{"filters": {"similarToRegion": {"assetId": "ID", "box": [0, 0, 1, 1]}}}""".Replace("ID", upper, StringComparison.Ordinal), out _, out _));
        Assert.False(SearchQueryJson.TryParse("""{"filters": {"similarToRegion": {"assetId": "ID", "box": [0, 0, 1, 1]}}}""".Replace("ID", notHex, StringComparison.Ordinal), out _, out _));
    }

    [Fact]
    public void Accepts_a_box_drawn_to_the_edge_by_the_slack_and_not_beyond_it()
    {
        var edge = (0.5 + (SimilarToRegionFilter.BoxSlack / 2)).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var over = (0.5 + (SimilarToRegionFilter.BoxSlack * 2)).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(SearchQueryJson.TryParse(WithId("""{"filters": {"similarToRegion": {"assetId": "ID", "box": [0.5, 0, SPAN, 1]}}}""").Replace("SPAN", edge, StringComparison.Ordinal), out _, out _));
        Assert.False(SearchQueryJson.TryParse(WithId("""{"filters": {"similarToRegion": {"assetId": "ID", "box": [0.5, 0, SPAN, 1]}}}""").Replace("SPAN", over, StringComparison.Ordinal), out _, out _));
    }

    [Theory]
    [InlineData("""{"filters": {"similarToRegion": {"box": [0, 0, 1, 1]}}}""", "similarToRegion")]
    [InlineData("""{"filters": {"similarToRegion": {"assetId": "ABC", "box": [0, 0, 1, 1]}}}""", "similarToRegion.assetId")]
    [InlineData("""{"filters": {"similarToRegion": {"assetId": "AAA"}}}""", "similarToRegion")]
    public void Refuses_a_region_without_a_box_or_a_valid_photo(string json, string expected)
    {
        var json2 = json.Replace("AAA", s_id, StringComparison.Ordinal);

        Assert.False(SearchQueryJson.TryParse(json2, out _, out var error));
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Cannot_be_combined_with_a_description_another_photo_or_a_count()
    {
        const string Region = "\"similarToRegion\": {\"assetId\": \"ID\", \"box\": [0, 0, 1, 1]}";

        Assert.False(SearchQueryJson.TryParse(WithId("{\"filters\": {" + Region + ", \"visual\": {\"text\": \"snow\"}}}"), out _, out var visual));
        Assert.False(SearchQueryJson.TryParse(WithId("{\"filters\": {" + Region + ", \"similarTo\": {\"assetId\": \"ID\"}}}"), out _, out var similar));
        Assert.False(SearchQueryJson.TryParse(WithId("{\"filters\": {" + Region + "}, \"aggregate\": {\"count\": \"assets\"}}"), out _, out var count));
        Assert.Contains("visual", visual, StringComparison.Ordinal);
        Assert.Contains("similarToRegion", similar, StringComparison.Ordinal);
        Assert.Contains("aggregate", count, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_not_part_of_an_answer_to_a_question()
    {
        var json = WithId("""{"filters": {"similarToRegion": {"assetId": "ID", "box": [0, 0, 1, 1]}}}""");

        Assert.Contains("similarToRegion", QuestionService.Validate(json), StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_and_reads_a_box_in_an_address()
    {
        Assert.Equal("0.1,0.25,0.5,0.0833", SearchLinks.FormatBox([0.1, 0.25, 0.5, 0.08333333]));
        Assert.Equal([0.1, 0.25, 0.5, 0.0833], SearchLinks.ParseBox("0.1,0.25,0.5,0.0833"));
        Assert.All(
            new[] { null, "", "0.1,0.2,0.3", "a,b,c,d", "0.1,0.2,0.3,0", "0.9,0.2,0.3,0.1", "-0.1,0.2,0.3,0.1", "1e-1,0.2,0.3,0.1", "0.1,0.2,0.3,NaN" },
            value => Assert.Null(SearchLinks.ParseBox(value)));
    }

    [Fact]
    public void The_filter_bar_s_hidden_fields_make_the_same_search_and_narrow_it()
    {
        var query = SearchLinks.FromFields("2020", null, null, null, null, regionAsset: s_id, regionBox: "0.1,0.2,0.3,0.4", regionLimit: "200");

        var region = query.Filters.SimilarToRegion!;
        Assert.Equal((s_id, 200), (region.AssetId, region.Limit));
        Assert.Equal([2020], query.Filters.Years!);
        // The raised limit goes with it, as for similar photos (the query's own limit would cap it).
        Assert.Equal(200, query.Limit);
        Assert.Equal(region, SearchLinks.ToFields(query).SimilarToRegion);
    }

    [Fact]
    public void A_description_or_a_bad_box_or_asset_replaces_or_drops_the_region()
    {
        Assert.Null(SearchLinks.FromFields(null, null, null, null, null, "snow", regionAsset: s_id, regionBox: "0,0,1,1").Filters.SimilarToRegion);
        Assert.Null(SearchLinks.FromFields(null, null, null, null, null, regionAsset: s_id, regionBox: "0,0,2,1").Filters.SimilarToRegion);
        Assert.Null(SearchLinks.FromFields(null, null, null, null, null, regionAsset: "nope", regionBox: "0,0,1,1").Filters.SimilarToRegion);
        // A region wins over a photo's similar ones: the two cannot be searched together.
        var both = SearchLinks.FromFields(null, null, null, null, null, similar: s_id, regionAsset: s_id, regionBox: "0,0,1,1");
        Assert.Null(both.Filters.SimilarTo);
        Assert.NotNull(both.Filters.SimilarToRegion);
    }

    [Fact]
    public void Shows_more_in_steps_and_has_a_chip_that_removes_the_region_and_its_raised_limit()
    {
        var query = SearchLinks.Region(s_id, [0, 0, 0.5, 0.5]);

        var more = SimilarSearch.More(query)!;
        var most = SimilarSearch.More(more)!;

        Assert.Equal((SearchQuery.DefaultLimit, SearchQuery.DefaultLimit), (more.Filters.SimilarToRegion!.Limit, more.Limit));
        Assert.Equal(SearchQuery.MaxLimit, most.Filters.SimilarToRegion!.Limit);
        Assert.Null(SimilarSearch.More(most));
        var chip = Assert.Single(SearchChips.For(most, Cultures.Text()));
        Assert.Equal("Similar to part of a photo", chip.Label);
        Assert.Equal(new SearchQuery(), chip.Without);
    }

    [Fact]
    public void Groups_by_occasion_with_the_most_similar_first_and_the_ones_in_none_last()
    {
        var items = Enumerable.Range(1, 6).Select(n => new TimelineItem(Ids.Of(n), "photo", new DateTime(2020, 1, n), true, null)).ToList();
        var first = new OccasionSummary(1, new DateTime(2020, 1, 1), new DateTime(2020, 1, 2), null, 2, []);
        var second = new OccasionSummary(2, new DateTime(2020, 1, 3), new DateTime(2020, 1, 3), "Lisbon", 1, []);
        var occasionOf = new Dictionary<string, OccasionSummary>
        {
            [Ids.Of(1)] = second,
            [Ids.Of(2)] = first,
            [Ids.Of(3)] = second,
            [Ids.Of(5)] = first,
        };

        var groups = OccasionGroups.Group(items, occasionOf);

        Assert.Equal([2L, 1L, 0L], groups.Select(g => g.Occasion?.Id ?? 0));
        Assert.Equal([[Ids.Of(1), Ids.Of(3)], [Ids.Of(2), Ids.Of(5)], [Ids.Of(4), Ids.Of(6)]], groups.Select(g => g.Items.Select(i => i.Id).ToArray()));
        Assert.Empty(OccasionGroups.Group([], occasionOf));
    }

    [Fact]
    public void The_detail_page_offers_the_selection_of_a_photo_when_the_image_model_is_there()
    {
        var withModel = RenderDetail(Details.Photo(Ids.Of(1)), available: true);

        var form = withModel.Find("form[data-region-form]");
        Assert.Equal("get", form.GetAttribute("method"));
        // Shown by region.js; without it the page is as it was.
        Assert.True(form.HasAttribute("hidden"));
        Assert.Equal(Ids.Of(1), form.QuerySelector($"input[name='{SearchLinks.RegionAssetParameter}']")!.GetAttribute("value"));
        Assert.NotNull(form.QuerySelector($"input[name='{SearchLinks.RegionBoxParameter}'][data-region-input]"));
        Assert.Equal("1", form.QuerySelector("input[name='filter']")!.GetAttribute("value"));
        Assert.True(form.QuerySelector("[data-region-submit]")!.HasAttribute("disabled"));
        Assert.Equal("Select a part of the photo", withModel.Find("[data-region-toggle]").TextContent);
        Assert.Equal($"photos/{Ids.Of(1)}/preview-1280", withModel.Find("[data-region-frame] img").GetAttribute("src"));
        Assert.Equal("region-hint", withModel.Find("[data-region-frame]").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void The_detail_page_offers_nothing_without_the_image_model()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)), available: false);

        Assert.Empty(page.FindAll("[data-region]"));
        Assert.Equal($"photos/{Ids.Of(1)}/preview-1280", page.Find("figure.preview img").GetAttribute("src"));
    }

    [Fact]
    public void The_detail_page_offers_nothing_for_a_video()
    {
        var page = RenderDetail(Details.Video(Ids.Of(2)), available: true);

        Assert.Empty(page.FindAll("[data-region]"));
    }

    [Fact]
    public void The_region_text_is_in_both_languages()
    {
        foreach (var key in new[] { "Region_Select", "Region_Hint", "Region_FrameLabel", "Region_StatusSelected", "Region_StatusNone", "Region_Find", "Region_Clear", "Region_Unavailable", "Chip_SimilarToRegion", "Search_MostSimilarToRegion", "Search_NotInOccasion" })
        {
            Assert.NotEqual(key, Cultures.Text()[key].Value);
        }
    }

    private IRenderedComponent<PhotoDetail> RenderDetail(AssetDetail asset, bool available)
    {
        var library = new FakeLibrary();
        library.Assets[asset.Id] = asset;
        Services.AddSingleton<ILibraryQueries>(library);
        Services.AddSingleton<IRegionEmbedder>(new StubRegionEmbedder(available));
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/photos/{asset.Id}");
        return Render<PhotoDetail>(p => p.Add(x => x.Id, asset.Id));
    }

    private sealed class StubRegionEmbedder(bool available) : IRegionEmbedder
    {
        public bool IsAvailable => available;

        public Task<float[]> EmbedAsync(string assetId, IReadOnlyList<double> box, CancellationToken cancellationToken = default) =>
            Task.FromResult(FakeQueryEmbedder.Axis(0));
    }
}

/// <summary>The image model's loading and the cut of a region (S-041, D-099), with a fake preview and no model file.</summary>
public sealed class ClipRegionEmbedderTests
{
    [Fact]
    public async Task Is_unavailable_without_the_model_file_and_says_so_without_remembering_it()
    {
        var calls = 0;
        using var embedder = Embedder((out string? warning, out ClipLoadFailure failure) =>
        {
            calls++;
            (warning, failure) = ("The CLIP image model is not in /models.", ClipLoadFailure.Missing);
            return null;
        }, present: false, preview: null);

        Assert.False(embedder.IsAvailable);
        await Assert.ThrowsAsync<VisualSearchUnavailableException>(() => embedder.EmbedAsync(Ids.Of(1), [0, 0, 1, 1]));
        await Assert.ThrowsAsync<VisualSearchUnavailableException>(() => embedder.EmbedAsync(Ids.Of(1), [0, 0, 1, 1]));
        // A missing file may arrive later: it is tried again, never remembered as a failure.
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task An_unusable_model_stays_off_until_a_restart_and_an_unreadable_one_is_tried_again_later()
    {
        var clock = new MovableTime(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));
        var calls = 0;
        var failure = ClipLoadFailure.Invalid;
        using var embedder = Embedder((out string? warning, out ClipLoadFailure f) =>
        {
            calls++;
            (warning, f) = ("broken", failure);
            return null;
        }, present: true, preview: null, clock);

        Assert.True(embedder.IsAvailable);
        await Assert.ThrowsAsync<VisualSearchUnavailableException>(() => embedder.EmbedAsync(Ids.Of(1), [0, 0, 1, 1]));
        Assert.False(embedder.IsAvailable);
        await Assert.ThrowsAsync<VisualSearchUnavailableException>(() => embedder.EmbedAsync(Ids.Of(1), [0, 0, 1, 1]));
        Assert.Equal(1, calls);

        failure = ClipLoadFailure.Unreadable;
        using var unreadable = Embedder((out string? warning, out ClipLoadFailure f) =>
        {
            calls++;
            (warning, f) = ("locked", failure);
            return null;
        }, present: true, preview: null, clock);
        await Assert.ThrowsAsync<VisualSearchUnavailableException>(() => unreadable.EmbedAsync(Ids.Of(1), [0, 0, 1, 1]));
        Assert.False(unreadable.IsAvailable);
        clock.Now += ClipRegionEmbedder.RetryAfter;
        Assert.True(unreadable.IsAvailable);
    }

    [Fact]
    public async Task Refuses_a_box_that_is_not_inside_the_photo_before_loading_anything()
    {
        var calls = 0;
        using var embedder = Embedder((out string? warning, out ClipLoadFailure failure) =>
        {
            calls++;
            (warning, failure) = (null, ClipLoadFailure.None);
            return null;
        }, present: true, preview: null);

        await Assert.ThrowsAsync<ArgumentException>(() => embedder.EmbedAsync(Ids.Of(1), [0.5, 0.5, 0.6, 0.6]));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(HttpRequestException))]
    public async Task Answers_unavailable_not_an_error_page_when_the_preview_cannot_be_fetched(Type failure)
    {
        using var embedder = new ClipRegionEmbedder(
            "/models",
            (out string? warning, out ClipLoadFailure failureKind) =>
            {
                (warning, failureKind) = (null, ClipLoadFailure.Missing);
                return new FixedEncoder();
            },
            () => true,
            new ThrowingPreview((Exception)Activator.CreateInstance(failure, "storage is down")!),
            NullLogger<ClipRegionEmbedder>.Instance);

        var thrown = await Assert.ThrowsAsync<VisualSearchUnavailableException>(() => embedder.EmbedAsync(Ids.Of(1), [0, 0, 1, 1]));

        Assert.IsType(failure, thrown.InnerException);
    }

    [Fact]
    public async Task Says_the_photo_is_not_in_the_library_when_it_has_no_preview()
    {
        using var embedder = new ClipRegionEmbedder(
            "/models",
            (out string? warning, out ClipLoadFailure failureKind) =>
            {
                (warning, failureKind) = (null, ClipLoadFailure.Missing);
                return new FixedEncoder();
            },
            () => true,
            new FixedPreview(null),
            NullLogger<ClipRegionEmbedder>.Instance);

        await Assert.ThrowsAsync<AssetNotAnalysedException>(() => embedder.EmbedAsync(Ids.Of(1), [0, 0, 1, 1]));
    }

    private sealed class FixedEncoder : IClipImageEncoder
    {
        public float[] Encode(ReadOnlySpan<byte> rgb) => [1f];

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingPreview(Exception failure) : IPreviewSource
    {
        public Task<byte[]?> ReadAsync(string assetId, CancellationToken cancellationToken = default) => Task.FromException<byte[]?>(failure);
    }

    private static ClipRegionEmbedder Embedder(ClipRegionEmbedder.Loader load, bool present, byte[]? preview, TimeProvider? clock = null) =>
        new("/models", load, () => present, new FixedPreview(preview), NullLogger<ClipRegionEmbedder>.Instance, clock);

    private sealed class FixedPreview(byte[]? bytes) : IPreviewSource
    {
        public Task<byte[]?> ReadAsync(string assetId, CancellationToken cancellationToken = default) => Task.FromResult(bytes);
    }
}
