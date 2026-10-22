using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Worker.Sources;

namespace Sepiabox.Worker.Tests.Sources;

/// <summary>The sync reads its two secrets by name and never lists the vault (S-038, D-115): its role is on the secrets, not the vault.</summary>
public sealed class VaultSecretsTests
{
    [Fact]
    public async Task Gets_the_two_secrets_by_name_and_never_lists_the_vault()
    {
        var vault = new FakeVault { Secrets = { [VaultSecrets.GoogleClientId] = "the-id", [VaultSecrets.GoogleClientSecret] = "the-secret" } };

        var client = await VaultSecrets.ReadGoogleClientAsync(vault, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(("the-id", "the-secret"), client);
        Assert.Equal([VaultSecrets.GoogleClientId, VaultSecrets.GoogleClientSecret], vault.Got);
        Assert.Equal(0, vault.Listed);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    public async Task A_secret_that_is_missing_or_that_has_no_role_yet_is_not_set(int status)
    {
        var vault = new FakeVault { Secrets = { [VaultSecrets.GoogleClientId] = "the-id" }, FailWith = status };

        Assert.Null(await VaultSecrets.ReadGoogleClientAsync(vault, NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_values_are_not_set()
    {
        var vault = new FakeVault { Secrets = { [VaultSecrets.GoogleClientId] = "", [VaultSecrets.GoogleClientSecret] = "x" } };

        Assert.Null(await VaultSecrets.ReadGoogleClientAsync(vault, NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task Any_other_failure_is_not_hidden()
    {
        var vault = new FakeVault { FailWith = 500 };

        await Assert.ThrowsAsync<RequestFailedException>(() => VaultSecrets.ReadGoogleClientAsync(vault, NullLogger.Instance, CancellationToken.None));
    }

    private sealed class FakeVault : SecretClient
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public List<string> Got { get; } = [];

        public int Listed { get; private set; }

        public int? FailWith { get; init; }

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(string name, string? version = null, CancellationToken cancellationToken = default)
        {
            Got.Add(name);
            if (FailWith is { } status && (!Secrets.ContainsKey(name) || status == 500))
            {
                throw new RequestFailedException(status, "failed");
            }

            return Task.FromResult(Response.FromValue(new KeyVaultSecret(name, Secrets[name]), null!));
        }

        public override AsyncPageable<SecretProperties> GetPropertiesOfSecretsAsync(CancellationToken cancellationToken = default)
        {
            Listed++;
            throw new RequestFailedException(403, "listing is not allowed");
        }
    }
}
