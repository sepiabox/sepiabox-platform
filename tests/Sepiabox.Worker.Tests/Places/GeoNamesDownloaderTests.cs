using System.Net;
using System.Security.Cryptography;
using Sepiabox.Worker.Places;

namespace Sepiabox.Worker.Tests.Places;

public sealed class GeoNamesDownloaderTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly ListLogger _logger = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Downloads_the_three_files_over_https_from_geonames_and_logs_their_hash_size_and_date()
    {
        var geoNames = FakeGeoNames.WithSample();
        using var http = new HttpClient(geoNames);

        var source = await new GeoNamesDownloader(http, _logger).DownloadAsync(_folder.Path, CancellationToken.None);

        Assert.Equal(
            ["https://download.geonames.org/export/dump/cities500.zip", "https://download.geonames.org/export/dump/admin1CodesASCII.txt",
                "https://download.geonames.org/export/dump/countryInfo.txt"],
            geoNames.Requests.Select(u => u.ToString()));
        Assert.Equal(geoNames.Files["cities500.zip"], await File.ReadAllBytesAsync(source.Cities));
        Assert.Equal(5, GeoNamesFiles.ReadCities(source.Cities).Count());
        Assert.Equal(4, GeoNamesFiles.ReadAdmin1(source.Admin1).Count);
        Assert.Equal(2, GeoNamesFiles.ReadCountries(source.Countries).Count);

        var zip = geoNames.Files["cities500.zip"];
        var logged = Assert.Single(_logger.Messages, m => m.StartsWith("Downloaded cities500.zip", StringComparison.Ordinal));
        Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(zip)), logged, StringComparison.Ordinal);
        Assert.Contains($"{zip.Length} bytes", logged, StringComparison.Ordinal);
        Assert.Contains("2026-10-20 01:30:00Z", logged, StringComparison.Ordinal);
        Assert.Equal(3, _logger.Messages.Count(m => m.StartsWith("Downloaded ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("http://download.geonames.org/export/dump/")]
    [InlineData("https://example.com/export/dump/")]
    [InlineData("https://download.geonames.org.example.com/export/dump/")]
    [InlineData("https://download.geonames.org:8443/export/dump/")]
    public void Refuses_any_source_but_https_from_download_geonames_org(string baseUri)
    {
        using var http = new HttpClient(FakeGeoNames.WithSample());

        var error = Assert.Throws<InvalidOperationException>(() => new GeoNamesDownloader(http, _logger, new Uri(baseUri)));

        Assert.Contains("only downloaded over HTTPS from download.geonames.org", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_redirect_or_an_error_fails_the_download(HttpStatusCode status)
    {
        var geoNames = FakeGeoNames.WithSample();
        geoNames.Override = _ => new HttpResponseMessage(status) { Headers = { Location = new Uri("http://example.com/cities500.zip") } };
        using var http = new HttpClient(geoNames);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => new GeoNamesDownloader(http, _logger).DownloadAsync(_folder.Path, CancellationToken.None));

        Assert.Equal(status, error.StatusCode);
        Assert.Single(geoNames.Requests);
        Assert.Empty(Directory.EnumerateFiles(_folder.Path));
    }

    [Fact]
    public void The_worker_s_client_never_follows_redirects()
    {
        using var http = GeoNamesDownloader.CreateClient();
        var handler = typeof(HttpMessageInvoker)
            .GetField("_handler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(http);

        Assert.False(Assert.IsType<SocketsHttpHandler>(handler).AllowAutoRedirect);
    }

    [Fact]
    public async Task A_file_larger_than_expected_fails_the_download()
    {
        var geoNames = FakeGeoNames.WithSample();
        geoNames.Override = _ =>
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentLength = GeoNamesDownloader.MaxFileBytes + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        using var http = new HttpClient(geoNames);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new GeoNamesDownloader(http, _logger).DownloadAsync(_folder.Path, CancellationToken.None));
    }
}
