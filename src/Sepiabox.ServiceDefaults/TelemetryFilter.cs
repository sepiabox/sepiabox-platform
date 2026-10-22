namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Which outgoing HTTP requests the tracing records (S-039). A pre-authenticated download address (OneDrive's carries <c>tempauth</c>, a
/// signed token in the query) is a credential for the length of its life: the instrumentation redacts query values by default, but that
/// is a setting somebody can turn off, so a request that carries one is not traced at all. Google Drive's downloads send the token in a
/// header, never in the address.
/// </summary>
public static class TelemetryFilter
{
    private static readonly string[] s_credentialParameters = ["tempauth", "access_token", "sig", "X-Goog-Signature"];

    public static bool IsTraced(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = request.RequestUri?.Query;
        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        return !s_credentialParameters.Any(name => query.Contains($"?{name}=", StringComparison.OrdinalIgnoreCase) || query.Contains($"&{name}=", StringComparison.OrdinalIgnoreCase));
    }
}
