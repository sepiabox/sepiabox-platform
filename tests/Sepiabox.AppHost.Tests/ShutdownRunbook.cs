namespace Sepiabox.AppHost.Tests;

/// <summary>
/// The resource names the €45 shutdown runbook stops (S-030): the defaults of the parameters of
/// <c>automation/Stop-Sepiabox.ps1</c> in sepiabox-infra, copied here because that repository is not
/// checked out with this one. Change both together (D-061).
/// </summary>
internal static class ShutdownRunbook
{
    public const string ResourceGroup = "rg-sepiabox";
    public const string WebApp = "sepiabox-web";
    public const string PostgresServer = "sepiabox-pg";
    public const string ServiceBusNamespace = "sepiabox-sb";
    public const string Queue = "manifests";
}
