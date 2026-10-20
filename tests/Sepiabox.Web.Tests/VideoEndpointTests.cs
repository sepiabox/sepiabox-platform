using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using AngleSharp.Html.Parser;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The real app on an in-memory library and a stopped clock: a video's detail page and links. Sign-in on
/// these addresses (anonymous, not on the list) is covered by the media theories in <see cref="SignInTests"/>.
/// </summary>
public sealed class VideoEndpointTests
{
    private static readonly string s_video = Ids.Of(21);
    private static readonly DateTimeOffset s_now = new(2026, 10, 20, 9, 7, 30, TimeSpan.Zero);

    // The 5-minute signing window that s_now falls in starts here.
    private static readonly DateTimeOffset s_windowStart = new(2026, 10, 20, 9, 5, 0, TimeSpan.Zero);

    [Fact]
    public async Task Redirects_the_720p_version_to_a_read_only_link_that_lasts_for_playback()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(new Uri($"/photos/{s_video}/proxy-720p", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.Equal(SasUrlFactory.RedirectCacheDuration, response.Headers.CacheControl?.MaxAge);
        var target = response.Headers.Location!;
        Assert.EndsWith($"/derivatives/{s_video}/proxy-720p.mp4", target.AbsolutePath, StringComparison.Ordinal);
        var query = HttpUtility.ParseQueryString(target.Query);
        Assert.Equal("r", query["sp"]);
        Assert.Equal("b", query["sr"]);
        Assert.Equal(s_windowStart + SasUrlFactory.VideoLifetime, Expiry(target));
    }

    [Theory]
    [InlineData(DerivativeNames.Poster)]
    [InlineData(DerivativeNames.Thumbnail)]
    public async Task Redirects_a_video_s_images_to_links_that_expire_within_15_minutes(string derivative)
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var response = await http.GetAsync(new Uri($"/photos/{s_video}/{derivative}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(s_windowStart + SasUrlFactory.DerivativeLifetime, Expiry(response.Headers.Location!));
        Assert.True(Expiry(response.Headers.Location!) <= s_now.AddMinutes(15));
    }

    [Fact]
    public async Task The_detail_page_plays_the_video_without_any_storage_link_and_loads_the_player_script()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient();

        var html = await http.GetStringAsync(new Uri($"/photos/{s_video}", UriKind.Relative));

        Assert.Contains($"src=\"photos/{s_video}/proxy-720p\"", html, StringComparison.Ordinal);
        Assert.Contains($"poster=\"photos/{s_video}/poster\"", html, StringComparison.Ordinal);
        using var document = await new HtmlParser().ParseDocumentAsync(html);
        var scripts = document.Scripts.Select(s => s.GetAttribute("src") ?? string.Empty).ToList();
        // The file name exactly, fingerprinted or not.
        Assert.Contains(scripts, src => Regex.IsMatch(src, @"^video(\.[a-z0-9]+)?\.js$"));
        Assert.DoesNotContain("sig=", html, StringComparison.Ordinal);
    }

    private static DateTimeOffset Expiry(Uri url) =>
        DateTimeOffset.Parse(HttpUtility.ParseQueryString(url.Query)["se"]!, CultureInfo.InvariantCulture);

    private static WebFactory Create()
    {
        var library = new FakeLibrary();
        library.Assets[s_video] = Details.Video(s_video);
        return new WebFactory(library) { Time = new FixedTime(s_now) };
    }
}
