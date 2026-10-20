using System.Text.RegularExpressions;

namespace Sepiabox.AppHost.Tests;

/// <summary>No secret is committed (S-034): configuration comes from user secrets, Key Vault and GitHub secrets.</summary>
public sealed partial class RepositorySecretsTests
{
    private static readonly string[] s_textExtensions =
        [".cs", ".csproj", ".json", ".yml", ".yaml", ".sh", ".ps1", ".bicep", ".md", ".razor", ".props", ".slnx", ".config", ".env", ".txt", ".xml"];

    // Where a "key: value" or "key = value" line is configuration, not code (in C#, `password: password` is an
    // argument name).
    private static readonly string[] s_configurationExtensions = [".json", ".yml", ".yaml", ".env", ".config", ".bicep"];

    // Directories that hold build output or local state, never committed.
    private static readonly string[] s_skipped = ["bin", "obj", ".git", ".azure", ".claude", "TestResults", "node_modules"];

    // The storage emulator's published account key: documented by Microsoft, so not a secret. It has no regex
    // metacharacters, so the scan's pattern uses it as it is.
    private const string EmulatorKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    [Fact]
    public void No_tracked_file_contains_a_key_a_token_a_signed_link_or_a_literal_secret()
    {
        var findings = Files(Repository.Root)
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, number: i + 1)))
            .Where(entry => IsSecret(entry.line, Path.GetExtension(entry.file)))
            .Select(entry => $"{Path.GetRelativePath(Repository.Root, entry.file)}:{entry.number}")
            .ToList();

        Assert.True(findings.Count == 0, "Possible secrets: " + string.Join(", ", findings));
    }

    /// <summary>Samples built at run time, so this repository never holds one the scan would flag.</summary>
    public static TheoryData<string, string> Samples => new()
    {
        { "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=" + new string('Q', 40) + "==", ".cs" },
        { "accountkey=" + new string('Q', 40), ".md" },
        { "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=root;SharedAccessKey=" + new string('Q', 40), ".cs" },
        { "https://x.blob.core.windows.net/c/b?sv=2024-11-04&se=2026&sp=r&sig=" + new string('Q', 30) + "%3D", ".md" },
        { "-----BEGIN " + "RSA PRIVATE KEY-----", ".txt" },
        { "-----BEGIN " + "PRIVATE KEY-----", ".md" },
        { "sk-" + "ant-" + "api03-" + new string('Q', 40), ".cs" },
        { "gh" + "p_" + new string('Q', 36), ".sh" },
        { "gh" + "s_" + new string('Q', 36), ".yml" },
        { "github_" + "pat_" + new string('Q', 40), ".md" },
        { "\"ClientSecret\": \"" + new string('Q', 12) + "\"", ".json" },
        { "  \"apiKey\" : \"" + new string('Q', 12) + "\",", ".json" },
        { "client_secret: " + new string('Q', 12), ".yml" },
        { "PASSWORD=" + new string('Q', 12), ".env" },
        { "  password: '" + new string('Q', 12) + "'", ".yaml" },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Scan_flags_each_kind_of_secret(string line, string extension)
    {
        Assert.True(IsSecret(line, extension), $"Not flagged: {line}");
    }

    [Theory]
    [InlineData("DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=" + EmulatorKey, ".cs")]
    [InlineData("          PG_PASSWORD: ${{ secrets.PG_PASSWORD }}", ".yml")]
    [InlineData("\"Password\": \"<password>\"", ".json")]
    [InlineData("param pg_password_value string", ".bicep")]
    [InlineData("await CatalogAppRole.EnsureAsync(connection, name, password: secret, token);", ".cs")]
    [InlineData("var password = ScramVerifier.Create(value);", ".cs")]
    public void Scan_leaves_references_placeholders_and_code_alone(string line, string extension)
    {
        Assert.False(IsSecret(line, extension), $"Flagged: {line}");
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

    private static bool IsSecret(string line, string extension) =>
        Token().IsMatch(line)
        || (s_configurationExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) && ConfiguredSecret().IsMatch(line));

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

    // Anywhere: a storage key (other than the emulator's) or a Service Bus key in a connection string, a SAS
    // signature, a PEM private key, an Anthropic API key, a GitHub token.
    [GeneratedRegex(
        @"AccountKey=(?!" + EmulatorKey + @")[A-Za-z0-9+/]{20}"
        + @"|SharedAccessKey=[A-Za-z0-9+/]{20}"
        + @"|[?&]sig=[A-Za-z0-9%+/]{20}"
        + @"|-----BEGIN [A-Z ]*PRIVATE KEY-----"
        + @"|sk-ant-[A-Za-z0-9_-]{20}"
        + @"|\bgh[pousr]_[A-Za-z0-9]{30}"
        + @"|\bgithub_pat_[A-Za-z0-9_]{20}",
        RegexOptions.IgnoreCase)]
    private static partial Regex Token();

    // In configuration: a client secret, API key or password, quoted or not, with a literal value (not a
    // placeholder in <>, nor a ${...} or {{...}} reference).
    [GeneratedRegex(
        @"[""']?\b(client_?secret|api_?key|password)[""']?\s*[:=]\s*[""']?(?![<$\{])[^""'\s<]{8,}",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConfiguredSecret();
}
