using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Sepiabox.Storage;
using Sepiabox.Web.Services;

namespace Sepiabox.Web;

/// <summary>
/// Host wiring for Azure (S-058), which D-051 allows in the composition root: Key Vault as a configuration
/// source, and the data protection key ring in Blob Storage protected with a Key Vault key. Both are on
/// only when the AppHost passes their settings, so local runs and tests keep the defaults.
/// </summary>
public static partial class AzureSetup
{
    /// <summary>The Key Vault connection (its URI) the AppHost passes in Azure.</summary>
    public const string VaultConnectionName = "vault";

    /// <summary>The versionless URI of the Key Vault key that wraps the key ring.</summary>
    public const string KeyUriKey = "DataProtection:KeyUri";

    /// <summary>The versionless URI of the Key Vault key that wraps the token ring (S-038, D-115).</summary>
    public const string TokenKeyUriKey = "DataProtection:TokenKeyUri";

    /// <summary>The key ring's blob in <see cref="StorageContainers.DataProtection"/>.</summary>
    public const string KeyRingBlobName = BlobLayout.KeyRingBlobName;

    /// <summary>Shared by every revision and replica, so they read each other's cookies.</summary>
    public const string ApplicationName = BlobLayout.KeyRingApplicationName;

    // Owner-created secrets (sign-in, the access list, the Anthropic key) are read again this often, so a
    // change to Access--Emails reaches the web without a restart (D-048).
    private static readonly TimeSpan s_secretsReloadInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reads every Key Vault secret as configuration, <c>--</c> becoming <c>:</c>
    /// (<c>Auth--Google--ClientId</c> is <c>Auth:Google:ClientId</c>). Only the owner creates them; a missing
    /// one is simply missing configuration, as locally (a provider without its keys is left out).
    /// </summary>
    public static WebApplicationBuilder AddSepiaboxSecrets(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString(VaultConnectionName)))
        {
            builder.Configuration.AddAzureKeyVaultSecrets(
                VaultConnectionName,
                options: new AzureKeyVaultConfigurationOptions { ReloadInterval = s_secretsReloadInterval });
        }

        return builder;
    }

    /// <summary>
    /// Keeps the data protection keys (which sign the sign-in cookie and antiforgery tokens) in Blob Storage,
    /// wrapped with a Key Vault key, so a restart, a new revision or a second replica keeps everyone signed
    /// in. Container Apps' own managed data protection is turned off for the web (D-062).
    /// </summary>
    public static WebApplicationBuilder AddSepiaboxKeyRing(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var keyUri = builder.Configuration[KeyUriKey];
        if (string.IsNullOrEmpty(keyUri))
        {
            return builder;
        }

        builder.Services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToAzureBlobStorage(services => services.GetRequiredService<BlobServiceClient>()
                .GetBlobContainerClient(StorageContainers.DataProtection)
                .GetBlobClient(KeyRingBlobName))
            // The managed identity, named by AZURE_CLIENT_ID, which the AppHost sets.
            .ProtectKeysWithAzureKeyVault(new Uri(keyUri), new DefaultAzureCredential());
        return builder;
    }

    /// <summary>
    /// The provider that protects the cloud accounts' refresh tokens (S-037, S-038, D-115). In Azure it is a ring of its own, in the
    /// <c>tokenring</c> container and wrapped by its own Key Vault key, so the sync job, which reads it, never holds the key that signs
    /// the sign-in cookies. Without <see cref="TokenKeyUriKey"/> (locally) it is the app's own ring.
    /// </summary>
    public static IDataProtectionProvider TokenProtectionProvider(IServiceProvider services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration[TokenKeyUriKey] is not { Length: > 0 } keyUri)
        {
            return services.GetRequiredService<IDataProtectionProvider>();
        }

        var ring = new ServiceCollection();
        ring.AddLogging();
        ring.AddDataProtection()
            .SetApplicationName(BlobLayout.TokenRingApplicationName)
            .PersistKeysToAzureBlobStorage(services.GetRequiredService<BlobServiceClient>()
                .GetBlobContainerClient(BlobLayout.TokenRingContainer)
                .GetBlobClient(BlobLayout.TokenRingBlobName))
            .ProtectKeysWithAzureKeyVault(new Uri(keyUri), new DefaultAzureCredential());
        return ring.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    /// <summary>Warns at start when the web runs in Production with keys that a restart would lose.</summary>
    public static WebApplication WarnAboutEphemeralKeys(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Environment.IsProduction() && string.IsNullOrEmpty(app.Configuration[KeyUriKey]))
        {
            LogNoKeyRing(app.Logger, KeyUriKey);
        }

        return app;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Key} is not set: sign-in cookies do not survive a restart or a new revision.")]
    private static partial void LogNoKeyRing(ILogger logger, string key);
}
