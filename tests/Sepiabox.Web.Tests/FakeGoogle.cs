using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.Google;

namespace Sepiabox.Web.Tests;

/// <summary>
/// Google's token and user info endpoints, as the Google handler calls them after the browser comes back
/// with a code. Any other address is an error, so a test can never reach the real Google.
/// </summary>
internal sealed class FakeGoogle(string userInfo) : HttpMessageHandler
{
    public List<string> Calls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        Calls.Add(url);
        var body = url == GoogleDefaults.TokenEndpoint
            ? """{"access_token":"fake-access-token","token_type":"Bearer","expires_in":3600}"""
            : url == GoogleDefaults.UserInformationEndpoint ? userInfo : null;

        return Task.FromResult(body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
