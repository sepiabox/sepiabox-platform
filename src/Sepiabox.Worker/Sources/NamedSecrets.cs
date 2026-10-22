using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// Loads only the named secrets of a Key Vault as configuration (S-038, D-115): the sync job may read the four of the Google and
/// Microsoft OAuth clients and no other secret of the owner's vault, and the roles say the same (one per secret).
/// </summary>
public sealed class NamedSecrets(IReadOnlyCollection<string> names) : KeyVaultSecretManager
{
    /// <summary>The two secrets of the Google OAuth client the sync reads; scripts/deploy.sh gives its identity a role on each.</summary>
    public static readonly IReadOnlyCollection<string> GoogleOAuthClient = ["Auth--Google--ClientId", "Auth--Google--ClientSecret"];

    /// <summary>The two secrets of the Microsoft app registration for OneDrive (S-039); the sign-in's <c>Auth--Microsoft--*</c> are not these.</summary>
    public static readonly IReadOnlyCollection<string> OneDriveOAuthClient = ["OneDrive--ClientId", "OneDrive--ClientSecret"];

    /// <summary>Every secret the sync job reads.</summary>
    public static readonly IReadOnlyCollection<string> All = [.. GoogleOAuthClient, .. OneDriveOAuthClient];

    public override bool Load(SecretProperties secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return names.Contains(secret.Name, StringComparer.OrdinalIgnoreCase);
    }
}
