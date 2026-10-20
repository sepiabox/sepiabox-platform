using System.Text.RegularExpressions;

namespace Sepiabox.AppHost.Tests;

/// <summary>No Azure secret is committed (S-034): configuration comes from user secrets, Key Vault and GitHub secrets.</summary>
public sealed partial class RepositorySecretsTests
{
    private static readonly string[] s_textExtensions =
        [".cs", ".csproj", ".json", ".yml", ".yaml", ".sh", ".ps1", ".bicep", ".md", ".razor", ".props", ".slnx", ".config", ".env", ".txt", ".xml"];

    // Directories that hold build output or local state, never committed.
    private static readonly string[] s_skipped = ["bin", "obj", ".git", ".azure", ".claude", "TestResults", "node_modules"];

    [Fact]
    public void No_tracked_file_contains_an_azure_key_a_signed_link_or_a_private_key()
    {
        var findings = Files(Repository.Root)
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, number: i + 1)))
            .Where(entry => Secret().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(Repository.Root, entry.file)}:{entry.number}")
            .ToList();

        Assert.True(findings.Count == 0, "Possible secrets: " + string.Join(", ", findings));
    }

    [Fact]
    public void Deploy_workflow_reads_the_database_parameters_only_from_github_secrets()
    {
        var workflow = File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "deploy.yml"));

        foreach (var name in new[] { "PG_USERNAME", "PG_PASSWORD", "PG_APP_PASSWORD" })
        {
            Assert.Contains($"{name}: ${{{{ secrets.{name} }}}}", workflow, StringComparison.Ordinal);
        }

        // The client id, tenant and subscription are identifiers, not secrets, so they are variables.
        Assert.DoesNotContain("secrets.AZURE_", workflow, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (s_textExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (!s_skipped.Contains(Path.GetFileName(child), StringComparer.Ordinal))
            {
                foreach (var file in Files(child))
                {
                    yield return file;
                }
            }
        }
    }

    // A storage or Service Bus key in a connection string (other than the storage emulator's published one,
    // which is not a secret), a SAS signature, a PEM private key, or a client secret, API key or password
    // given a literal value in JSON or YAML.
    [GeneratedRegex(
        @"AccountKey=(?!Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==)[A-Za-z0-9+/]{20}"
        + @"|SharedAccessKey=[A-Za-z0-9+/]{20}"
        + @"|[?&]sig=[A-Za-z0-9%+/]{20}"
        + @"|-----BEGIN [A-Z ]*PRIVATE KEY-----"
        + @"|""(ClientSecret|ApiKey|Password)""\s*:\s*""[^""<]{8,}""")]
    private static partial Regex Secret();
}
