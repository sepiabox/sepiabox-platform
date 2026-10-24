namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Which outgoing HTTP requests the tracing records (S-039). A pre-authenticated download address (OneDrive's carries <c>tempauth</c> in the query or an opaque
/// segment in the path) is a credential for the length of its life: the instrumentation redacts query values by default, but that is a
/// setting somebody can turn off and it never redacts a path, so a request to a download host, or one whose query carries a credential, is
/// not traced at all. Google Drive's downloads send the token in a
/// header, never in the address.
/// </summary>
public static class TelemetryFilter
{
    private static readonly string[] s_credentialParameters = ["tempauth", "access_token", "sig", "X-Goog-Signature", "psid"];

    // Where a cloud file is downloaded from after the API gave a pre-authenticated address: a personal OneDrive puts the credential in
    // the path (an opaque segment on a files.1drv.com host) or in a query, Drive's content hosts the same. None of these is traced, whatever
    // the address looks like; the API hosts (graph.microsoft.com, login.microsoftonline.com, *.googleapis.com) are traced as any other.
    private static readonly string[] s_downloadHostSuffixes =
    [
        ".1drv.com", ".1drv.ms", ".livefilestore.com", ".sharepoint.com", ".googleusercontent.com", "drive.usercontent.google.com",
    ];

    public static bool IsTraced(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri?.Host is { Length: > 0 } host
            && s_downloadHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                || string.Equals("." + host, suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var query = request.RequestUri?.Query;
        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        return !s_credentialParameters.Any(name => query.Contains($"?{name}=", StringComparison.OrdinalIgnoreCase) || query.Contains($"&{name}=", StringComparison.OrdinalIgnoreCase));
    }
}
