using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker.Tests.Places;

/// <summary>
/// GeoNames' download server, in memory: no test ever reaches download.geonames.org. Serves the synthetic
/// sample files by name and records what was asked for.
/// </summary>
internal sealed class FakeGeoNames : HttpMessageHandler
{
    public static readonly DateTimeOffset LastModified = new(2026, 10, 20, 1, 30, 0, TimeSpan.Zero);

    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public List<Uri> Requests { get; } = [];

    /// <summary>Answers every request with this instead, when set.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Override { get; set; }

    public static FakeGeoNames WithSample(int cities = 5)
    {
        var fake = new FakeGeoNames();
        fake.Files["cities500.zip"] = GeoNamesSample.Zip(cities);
        fake.Files["admin1CodesASCII.txt"] = Encoding.UTF8.GetBytes(GeoNamesSample.Admin1);
        fake.Files["countryInfo.txt"] = Encoding.UTF8.GetBytes(GeoNamesSample.Countries);
        return fake;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        if (Override is { } answer)
        {
            return Task.FromResult(answer(request));
        }

        var name = request.RequestUri!.Segments[^1];
        if (request.RequestUri.AbsolutePath != "/export/dump/" + name || !Files.TryGetValue(name, out var content))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        response.Content.Headers.LastModified = LastModified;
        return Task.FromResult(response);
    }
}

internal sealed class ListLogger : ILogger
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception));
}
