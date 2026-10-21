using Sepiabox.Catalog.Events;

namespace Sepiabox.Worker.Tests.Events;

/// <summary>The split rules of occasions (S-023, D-019): more than 6 hours, or more than 50 km. Synthetic points only.</summary>
public sealed class EventBuilderTests
{
    private static readonly DateTime s_start = new(2024, 12, 14, 10, 0, 0);

    // Lisbon, and points due north of it: a degree of latitude is about 111.2 km.
    private const double LisbonLatitude = 38.72;
    private const double LisbonLongitude = -9.14;

    [Fact]
    public void Photos_7_hours_apart_are_in_different_occasions()
    {
        var drafts = EventBuilder.Build([Photo("a", 0), Photo("b", 7)]);

        Assert.Equal([["a"], ["b"]], drafts.Select(d => d.AssetIds));
    }

    [Fact]
    public void A_gap_of_exactly_6_hours_is_still_one_occasion_and_one_second_more_is_not()
    {
        Assert.Single(EventBuilder.Build([Photo("a", 0), Photo("b", 6)]));
        Assert.Equal(2, EventBuilder.Build([Photo("a", 0), Photo("b", 6) with { CapturedAt = s_start.AddHours(6).AddSeconds(1) }]).Count);
    }

    [Fact]
    public void The_gap_is_to_the_previous_asset_so_a_long_day_of_photos_stays_together()
    {
        var drafts = EventBuilder.Build(Enumerable.Range(0, 10).Select(hour => Photo($"p{hour}", hour * 5)));

        Assert.Single(drafts);
        Assert.Equal(s_start.AddHours(45), drafts[0].EndsAt);
    }

    [Fact]
    public void Photos_1_hour_and_60_km_apart_are_in_different_occasions()
    {
        var drafts = EventBuilder.Build([Photo("a", 0, LisbonLatitude), Photo("b", 1, LisbonLatitude + 60 / 111.195)]);

        Assert.Equal([["a"], ["b"]], drafts.Select(d => d.AssetIds));
    }

    [Theory]
    [InlineData(49.9, 1)]
    [InlineData(50.1, 2)]
    public void The_distance_limit_is_50_km(double kilometres, int occasions)
    {
        var drafts = EventBuilder.Build([Photo("a", 0, LisbonLatitude), Photo("b", 1, LisbonLatitude + kilometres / 111.195)]);

        Assert.Equal(occasions, drafts.Count);
    }

    [Fact]
    public void Exactly_50_km_is_still_one_occasion_and_a_hair_more_is_not()
    {
        Assert.False(EventBuilder.TooFar(50.0));
        Assert.True(EventBuilder.TooFar(50.0000001));
        // The point 50 km due north by the same formula: measured as 50 km, give or take rounding, and not a split.
        var north = Photo("b", 1, LisbonLatitude + 50 / 6371.0088 * 180 / Math.PI);
        Assert.Equal(50.0, EventBuilder.DistanceKm(Photo("a", 0, LisbonLatitude), north), 6);
    }

    [Fact]
    public void The_distance_is_to_the_previous_geotagged_asset_not_to_the_previous_asset()
    {
        // b has no location: c is measured from a, 60 km away, so it starts another occasion.
        var drafts = EventBuilder.Build([
            Photo("a", 0, LisbonLatitude),
            Photo("b", 1),
            Photo("c", 2, LisbonLatitude + 60 / 111.195),
        ]);

        Assert.Equal([["a", "b"], ["c"]], drafts.Select(d => d.AssetIds));
    }

    [Fact]
    public void The_distance_does_not_reach_back_into_an_earlier_occasion()
    {
        // c is far from a, but b (7 hours later) started a new occasion with no geotagged asset yet.
        var drafts = EventBuilder.Build([Photo("a", 0, LisbonLatitude), Photo("b", 7), Photo("c", 8, LisbonLatitude + 1)]);

        Assert.Equal([["a"], ["b", "c"]], drafts.Select(d => d.AssetIds));
    }

    [Fact]
    public void The_order_of_the_input_does_not_matter()
    {
        var assets = new[] { Photo("d", 20), Photo("a", 0), Photo("c", 8), Photo("b", 1) };

        var forward = EventBuilder.Build(assets);
        var backward = EventBuilder.Build(assets.Reverse());

        Assert.Equal(forward.Select(d => d.AssetIds), backward.Select(d => d.AssetIds));
        Assert.Equal([["a", "b"], ["c"], ["d"]], forward.Select(d => d.AssetIds));
    }

    [Fact]
    public void Assets_taken_at_the_same_second_are_ordered_by_id()
    {
        var drafts = EventBuilder.Build([Photo("b", 0), Photo("a", 0)]);

        Assert.Equal(["a", "b"], drafts.Single().AssetIds);
    }

    [Fact]
    public void An_occasion_spans_its_first_and_last_capture_times()
    {
        var draft = EventBuilder.Build([Photo("a", 0), Photo("b", 3), Photo("c", 6)]).Single();

        Assert.Equal((s_start, s_start.AddHours(6)), (draft.StartsAt, draft.EndsAt));
    }

    [Fact]
    public void Nothing_in_gives_nothing_out()
    {
        Assert.Empty(EventBuilder.Build([]));
    }

    [Fact]
    public void The_place_label_is_the_commonest_city_and_the_first_in_order_when_tied()
    {
        var drafts = EventBuilder.Build([
            Photo("a", 0, city: "Sintra"),
            Photo("b", 1, city: "Lisbon"),
            Photo("c", 2, city: "Lisbon"),
            Photo("d", 20, city: "Porto"),
            Photo("e", 21, city: "Faro"),
        ]);

        Assert.Equal(["Lisbon", "Faro"], drafts.Select(d => d.PlaceLabel));
    }

    [Fact]
    public void The_place_label_falls_back_to_the_country_and_is_null_without_any_place()
    {
        var drafts = EventBuilder.Build([Photo("a", 0, country: "Portugal"), Photo("b", 20)]);

        Assert.Equal(["Portugal", null], drafts.Select(d => d.PlaceLabel));
    }

    [Fact]
    public void The_centroid_is_the_mean_of_the_geotagged_assets_and_null_without_any()
    {
        var drafts = EventBuilder.Build([
            Photo("a", 0, 38.0, -9.0),
            Photo("b", 1, 38.2, -9.0),
            Photo("c", 2),
            Photo("d", 20),
        ]);

        Assert.Equal(38.1, drafts[0].CentroidLatitude!.Value, 2);
        Assert.Equal(-9.0, drafts[0].CentroidLongitude!.Value, 2);
        Assert.Null(drafts[1].CentroidLatitude);
        Assert.Null(drafts[1].CentroidLongitude);
    }

    [Fact]
    public void The_centroid_of_points_either_side_of_the_180th_meridian_is_on_it()
    {
        var draft = EventBuilder.Build([Photo("a", 0, -17.0, 179.9), Photo("b", 1, -17.0, -179.9)]).Single();

        Assert.Equal(-17.0, draft.CentroidLatitude!.Value, 1);
        Assert.Equal(180.0, Math.Abs(draft.CentroidLongitude!.Value), 1);
    }

    [Fact]
    public void Measures_the_distance_between_lisbon_and_porto_as_about_274_km()
    {
        var lisbon = Photo("a", 0, 38.7223, -9.1393);
        var porto = Photo("b", 0, 41.1579, -8.6291);

        Assert.InRange(EventBuilder.DistanceKm(lisbon, porto), 270, 278);
    }

    private static EventAsset Photo(string id, int hours, double? latitude = null, double? longitude = null, string? city = null, string? country = null) =>
        new(id, s_start.AddHours(hours), latitude, latitude is null ? null : longitude ?? LisbonLongitude, city, country);
}
