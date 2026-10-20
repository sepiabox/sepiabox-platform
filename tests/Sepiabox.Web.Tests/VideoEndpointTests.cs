using System.Globalization;
using System.Net;
using System.Web;

namespace Sepiabox.Web.Tests;

/// <summary>The real app on an in-memory library: the detail page and the 720p link of a video, behind sign-in.</summary>
public sealed class VideoEndpointTests
{
    private static readonly string s_video = Ids.Of(21);

    [Fact]
    public async Task The_720p_version_redirects_to_a_read_only_link_that_lasts_for_playback()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(followRedirects: false);

        var before = DateTimeOffset.UtcNow;
        using var response = await http.GetAsync(new Uri($"/fotos/{s_video}/proxy-720p", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.Equal(TimeSpan.FromSeconds(540), response.Headers.CacheControl?.MaxAge);
        var target = response.Headers.Location!;
        Assert.EndsWith($"/derivatives/{s_video}/proxy-720p.mp4", target.AbsolutePath, StringComparison.Ordinal);
        var query = HttpUtility.ParseQueryString(target.Query);
        Assert.Equal("r", query["sp"]);
        Assert.Equal("b", query["sr"]);
        var expiry = DateTimeOffset.Parse(query["se"]!, CultureInfo.InvariantCulture);
        Assert.InRange(expiry, before.AddHours(4).AddMinutes(-6), DateTimeOffset.UtcNow.AddHours(4));
    }

    [Fact]
    public async Task The_detail_page_plays_the_video_without_any_storage_link()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient();

        var html = await http.GetStringAsync(new Uri($"/fotos/{s_video}", UriKind.Relative));

        Assert.Contains($"<video controls preload=\"metadata\" playsinline src=\"fotos/{s_video}/proxy-720p\" poster=\"fotos/{s_video}/poster\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Challenges_an_anonymous_request_for_the_video_and_never_redirects_it_to_storage()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(new Uri($"/fotos/{s_video}/proxy-720p", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("/entrar", location.AbsolutePath);
        Assert.DoesNotContain("sig=", location.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("devstoreaccount1", location.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answers_403_to_the_video_for_a_signed_in_user_not_on_the_list()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var response = await http.GetAsync(new Uri($"/fotos/{s_video}/proxy-720p", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("sig=", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static WebFactory Create()
    {
        var library = new FakeLibrary();
        library.Assets[s_video] = Details.Video(s_video);
        return new WebFactory(library);
    }
}
