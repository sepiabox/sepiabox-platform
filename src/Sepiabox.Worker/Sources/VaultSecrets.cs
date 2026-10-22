using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// Reads the Google and OneDrive OAuth clients from the owner's Key Vault by name (S-038, D-115). The sync identity has Secrets User on these
/// secrets and no role on the vault, so it must never <b>list</b> the vault (that needs a vault-level permission and would be refused):
/// each secret is a get by name. A secret that is not there, or that the identity has no role on yet, reads as not set.
/// </summary>
public static class VaultSecrets
{
    public const string GoogleClientId = "Auth--Google--ClientId";
    public const string GoogleClientSecret = "Auth--Google--ClientSecret";

    /// <summary>The Microsoft app registration for OneDrive (S-039): its own, not the sign-in's <c>Auth--Microsoft--*</c>.</summary>
    public const string OneDriveClientId = "OneDrive--ClientId";
    public const string OneDriveClientSecret = "OneDrive--ClientSecret";

    /// <summary>The id and the secret of the Google client, or null when either is missing or empty.</summary>
    public static Task<(string Id, string Secret)?> ReadGoogleClientAsync(SecretClient vault, ILogger logger, CancellationToken cancellationToken) =>
        ReadClientAsync(vault, GoogleClientId, GoogleClientSecret, logger, cancellationToken);

    /// <summary>The id and the secret of the OneDrive client, or null when either is missing or empty.</summary>
    public static Task<(string Id, string Secret)?> ReadOneDriveClientAsync(SecretClient vault, ILogger logger, CancellationToken cancellationToken) =>
        ReadClientAsync(vault, OneDriveClientId, OneDriveClientSecret, logger, cancellationToken);

    private static async Task<(string Id, string Secret)?> ReadClientAsync(
        SecretClient vault, string idName, string secretName, ILogger logger, CancellationToken cancellationToken)
    {
        var id = await ReadAsync(vault, idName, logger, cancellationToken);
        var secret = await ReadAsync(vault, secretName, logger, cancellationToken);
        return id is { Length: > 0 } && secret is { Length: > 0 } ? (id, secret) : null;
    }

    private static async Task<string?> ReadAsync(SecretClient vault, string name, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var response = await vault.GetSecretAsync(name, null, cancellationToken);
            return response.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status is 403 or 404)
        {
            // Not created yet, or the deploy has not given the identity its role on it yet: not set, which is not a fault of its own.
            Log.VaultSecretUnavailable(logger, name, ex.Status);
            return null;
        }
    }
}
