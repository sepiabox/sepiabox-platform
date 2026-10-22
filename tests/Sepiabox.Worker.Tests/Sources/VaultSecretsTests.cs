using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Worker.Sources;

namespace Sepiabox.Worker.Tests.Sources;

/// <summary>The sync asks for exactly its two secrets by name (S-038, D-115): its role is on those secrets, not on the vault.</summary>
public sealed class VaultSecretsTests
{
    [Fact]
    public async Task Asks_for_the_two_secrets_by_name_and_for_nothing_else()
    {
        var source = new FakeSecrets { Values = { [VaultSecrets.GoogleClientId] = "the-id", [VaultSecrets.GoogleClientSecret] = "the-secret" } };

        var client = await VaultSecrets.ReadGoogleClientAsync(source, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(("the-id", "the-secret"), client);
        Assert.Equal(["Auth--Google--ClientId", "Auth--Google--ClientSecret"], source.Asked);
    }

    [Fact]
    public async Task A_secret_that_is_not_there_is_not_set()
    {
        var source = new FakeSecrets { Values = { [VaultSecrets.GoogleClientId] = "the-id" } };

        Assert.Null(await VaultSecrets.ReadGoogleClientAsync(source, NullLogger.Instance, CancellationToken.None));
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("x", "")]
    public async Task Empty_values_are_not_set(string id, string secret)
    {
        var source = new FakeSecrets { Values = { [VaultSecrets.GoogleClientId] = id, [VaultSecrets.GoogleClientSecret] = secret } };

        Assert.Null(await VaultSecrets.ReadGoogleClientAsync(source, NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task A_failure_of_the_source_is_not_hidden()
    {
        var source = new FakeSecrets { Failure = new InvalidOperationException("vault down") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => VaultSecrets.ReadGoogleClientAsync(source, NullLogger.Instance, CancellationToken.None));
    }

    private sealed class FakeSecrets : ISecretSource
    {
        public Dictionary<string, string> Values { get; } = [];

        public List<string> Asked { get; } = [];

        public Exception? Failure { get; init; }

        public Task<string?> GetAsync(string name, CancellationToken cancellationToken)
        {
            Asked.Add(name);
            return Failure is { } failure ? Task.FromException<string?>(failure) : Task.FromResult(Values.GetValueOrDefault(name));
        }
    }
}
