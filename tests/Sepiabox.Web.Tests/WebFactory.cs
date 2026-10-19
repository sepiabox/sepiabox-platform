using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The real web app on a test catalog database. Blob links are signed offline with the emulator's
/// well-known key, so nothing is fetched from storage. S-010 extends this with sign-in.
/// </summary>
public class WebFactory(string catalogConnectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseSetting("ConnectionStrings:catalog", catalogConnectionString)
        .UseSetting("ConnectionStrings:blobs", "UseDevelopmentStorage=true");

    /// <summary>A client that reports redirects instead of following them to storage.</summary>
    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
