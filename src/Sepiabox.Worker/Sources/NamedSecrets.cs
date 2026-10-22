using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// Loads only the named secrets of a Key Vault as configuration (S-038, D-115): the sync job may read the two of the Google OAuth client
/// and no other secret of the owner's vault, and the roles say the same (one per secret).
/// </summary>
public sealed class NamedSecrets(IReadOnlyCollection<string> names) : KeyVaultSecretManager
{
    /// <summary>The two secrets of the Google OAuth client the sync reads; scripts/deploy.sh gives its identity a role on each.</summary>
    public static readonly IReadOnlyCollection<string> GoogleOAuthClient = ["Auth--Google--ClientId", "Auth--Google--ClientSecret"];

    public override bool Load(SecretProperties secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return names.Contains(secret.Name, StringComparer.OrdinalIgnoreCase);
    }
}
