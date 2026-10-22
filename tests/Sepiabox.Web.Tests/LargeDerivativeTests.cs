using System.Globalization;
using System.Net;
using System.Web;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The larger derivatives of an index-only work folder (S-063): their names are served like the others (S-064 shows them).</summary>
public sealed class LargeDerivativeTests
{
    private static readonly string s_asset = Ids.Of(21);
    private static readonly DateTimeOffset s_now = new(2026, 10, 20, 9, 7, 30, TimeSpan.Zero);

    [Theory]
    [InlineData("preview-2048", true)]
    [InlineData("proxy-1080p", true)]
    [InlineData("preview-1280", true)]
    [InlineData("proxy-720p", true)]
    [InlineData("preview-4096", false)]
    [InlineData("proxy-2160p", false)]
    public void Serves_exactly_the_derivatives_it_names(string name, bool served)
    {
        Assert.Equal(served, DerivativeNames.IsServed(name));
    }

    [Theory]
    [InlineData("proxy-720p", true)]
    [InlineData("proxy-1080p", true)]
    [InlineData("preview-2048", false)]
    [InlineData("poster", false)]
    public void Treats_both_playable_versions_as_video(string name, bool video)
    {
        Assert.Equal(video, DerivativeNames.IsVideo(name));
    }

    [Fact]
    public async Task Redirects_the_1080p_version_to_a_read_only_link_that_lasts_for_playback()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(new Uri($"/photos/{s_asset}/proxy-1080p", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var target = response.Headers.Location!;
        Assert.EndsWith($"/derivatives/{s_asset}/proxy-1080p.mp4", target.AbsolutePath, StringComparison.Ordinal);
        var window = new DateTimeOffset(2026, 10, 20, 9, 5, 0, TimeSpan.Zero);
        Assert.Equal(window + SasUrlFactory.VideoLifetime, DateTimeOffset.Parse(HttpUtility.ParseQueryString(target.Query)["se"]!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Redirects_the_2048_preview_to_a_link_that_expires_within_15_minutes()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(new Uri($"/photos/{s_asset}/preview-2048", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var target = response.Headers.Location!;
        Assert.EndsWith($"/derivatives/{s_asset}/preview-2048.webp", target.AbsolutePath, StringComparison.Ordinal);
        var expires = DateTimeOffset.Parse(HttpUtility.ParseQueryString(target.Query)["se"]!, CultureInfo.InvariantCulture);
        Assert.True(expires <= s_now.AddMinutes(15));
    }

    private static WebFactory Create()
    {
        var library = new FakeLibrary();
        library.Assets[s_asset] = Details.Video(s_asset);
        return new WebFactory(library) { Time = new FixedTime(s_now) };
    }
}
