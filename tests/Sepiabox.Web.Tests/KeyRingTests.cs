using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sepiabox.Web.Tests;

/// <summary>Where the web keeps the keys that sign its cookies (S-058, D-062).</summary>
public sealed class KeyRingTests
{
    private const string KeyUri = "https://vault-example.vault.azure.net/keys/dataprotection";

    [Fact]
    public async Task Keys_are_kept_in_blob_storage_and_wrapped_with_the_key_vault_key_in_production()
    {
        await using var web = new WebFactory(new FakeLibrary())
        {
            Environment = "Production",
            Settings = new() { [AzureSetup.KeyUriKey] = KeyUri },
        };

        var options = web.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        var application = web.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        // Both types are internal to the Azure packages; their names are what identifies them.
        Assert.Equal("AzureBlobXmlRepository", options.XmlRepository?.GetType().Name);
        Assert.Equal("AzureKeyVaultXmlEncryptor", options.XmlEncryptor?.GetType().Name);
        Assert.Equal(AzureSetup.ApplicationName, application.ApplicationDiscriminator);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Keys_stay_with_the_default_ring_when_no_key_vault_key_is_configured(string environment)
    {
        await using var web = new WebFactory(new FakeLibrary()) { Environment = environment };

        var options = web.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.NotEqual("AzureBlobXmlRepository", options.XmlRepository?.GetType().Name);
        Assert.Null(options.XmlEncryptor);
    }

    [Fact]
    public void Key_ring_container_is_the_one_the_deploy_creates()
    {
        Assert.Equal("dataprotection", Sepiabox.Web.Services.StorageContainers.DataProtection);
    }
}
