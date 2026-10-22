using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// Where the sync reads a named secret from (D-051: the Key Vault SDK stays in the composition root, <c>Program.cs</c>). The sync identity has
/// Secrets User on two secrets and no role on the vault, so an implementation gets one secret by name and never lists the vault.
/// Null when the secret is not there or the identity has no role on it yet.
/// </summary>
public interface ISecretSource
{
    Task<string?> GetAsync(string name, CancellationToken cancellationToken);
}

/// <summary>The Google OAuth client the sync reads by name from the owner's Key Vault (S-038, D-115): the two names, and when it counts as not set.</summary>
public static class VaultSecrets
{
    public const string GoogleClientId = "Auth--Google--ClientId";
    public const string GoogleClientSecret = "Auth--Google--ClientSecret";

    /// <summary>The id and the secret of the Google client, or null when either is missing or empty.</summary>
    public static async Task<(string Id, string Secret)?> ReadGoogleClientAsync(ISecretSource source, ILogger logger, CancellationToken cancellationToken)
    {
        var id = await source.GetAsync(GoogleClientId, cancellationToken);
        var secret = await source.GetAsync(GoogleClientSecret, cancellationToken);
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret))
        {
            Log.VaultSecretUnavailable(logger, string.IsNullOrEmpty(id) ? GoogleClientId : GoogleClientSecret);
            return null;
        }

        return (id, secret);
    }
}
