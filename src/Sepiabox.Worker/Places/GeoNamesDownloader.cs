using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker.Places;

/// <summary>
/// Downloads the three GeoNames dump files <c>geonames load</c> reads (S-059, D-067). Only over HTTPS from
/// <see cref="Host"/>, without following redirects, so the files can only come from GeoNames. Their content
/// cannot be pinned (GeoNames republishes them daily, D-066), so each file's SHA-256, size and date are logged
/// instead, and the load that follows checks the number of cities (<see cref="GeoNamesLoadGuard"/>).
/// </summary>
public sealed class GeoNamesDownloader
{
    public const string Host = "download.geonames.org";

    public static readonly Uri DefaultBaseUri = new("https://" + Host + "/export/dump/");

    public const string CitiesFile = "cities500.zip";
    public const string Admin1File = "admin1CodesASCII.txt";
    public const string CountriesFile = "countryInfo.txt";

    /// <summary>cities500.zip is about 10 MB; anything far larger is not the file we expect.</summary>
    public const long MaxFileBytes = 100L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly ILogger _logger;

    public GeoNamesDownloader(HttpClient http, ILogger logger, Uri? baseUri = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseUri = baseUri ?? DefaultBaseUri;
        EnsureGeoNames(_baseUri);
    }

    /// <summary>The client the worker downloads with: no redirects, so a redirect fails instead of leaving GeoNames.</summary>
    public static HttpClient CreateClient() =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Downloads the files into <paramref name="folder"/>, which must exist, and names them for the load.</summary>
    public async Task<GeoNamesSource> DownloadAsync(string folder, CancellationToken cancellationToken)
    {
        var cities = await DownloadFileAsync(folder, CitiesFile, cancellationToken);
        var admin1 = await DownloadFileAsync(folder, Admin1File, cancellationToken);
        var countries = await DownloadFileAsync(folder, CountriesFile, cancellationToken);
        return new GeoNamesSource(cities, admin1, countries);
    }

    private async Task<string> DownloadFileAsync(string folder, string name, CancellationToken cancellationToken)
    {
        var uri = new Uri(_baseUri, name);
        EnsureGeoNames(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // A redirect lands here too: it is never followed.
            throw new HttpRequestException(
                $"{uri} answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength is > MaxFileBytes)
        {
            throw new InvalidDataException($"{uri} is {response.Content.Headers.ContentLength} bytes, more than the {MaxFileBytes} expected at most.");
        }

        var path = Path.Combine(folder, name);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                size += read;
                if (size > MaxFileBytes)
                {
                    throw new InvalidDataException($"{uri} is more than the {MaxFileBytes} bytes expected at most.");
                }

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        var lastModified = response.Content.Headers.LastModified?.ToString("u", CultureInfo.InvariantCulture) ?? "unknown";
        var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        Log.GeoNamesFileDownloaded(_logger, name, sha256, size, lastModified, uri);
        return path;
    }

    private static void EnsureGeoNames(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort)
        {
            throw new InvalidOperationException($"GeoNames files are only downloaded over HTTPS from {Host}, not from {uri}.");
        }
    }
}
