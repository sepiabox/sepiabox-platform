using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The UI text files themselves (D-050): every English text has a Brazilian Portuguese translation, and the
/// translation never uses European Portuguese wording.
/// </summary>
public sealed partial class ResourceTests
{
    // European Portuguese words and constructions with a different Brazilian form: descarregar (baixar),
    // ecrã (tela), ficheiro (arquivo), logótipo (logotipo), ligação (conexão), câmara (câmera),
    // utilizador (usuário), telemóvel (celular), registo (registro), palavra-passe (senha),
    // "a carregar" and the like (carregando), "correu mal" (deu errado).
    private static readonly string[] EuropeanPortuguese =
    [
        @"descarreg\w*", @"ecr[ãa]s?", @"ficheiros?", @"log[óo]tipos?", @"liga[çc][ãa]o", @"liga[çc][õo]es",
        @"c[âa]maras?", @"utilizador\w*", @"telem[óo]ve(l|is)", @"registos?", @"palavra-passe",
        @"(a|A) (carregar|restabelecer|processar|reproduzir|enviar)", @"correu mal",
    ];

    [Fact]
    public void Every_english_text_has_a_non_empty_brazilian_portuguese_translation()
    {
        var english = Read("SharedResource.resx");
        var portuguese = Read("SharedResource.pt-BR.resx");

        Assert.NotEmpty(english);
        Assert.Empty(english.Keys.Except(portuguese.Keys));
        // A key only in the translation is text the app never shows in English.
        Assert.Empty(portuguese.Keys.Except(english.Keys));
        Assert.All(english, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{entry.Key} is empty in English."));
        Assert.All(portuguese, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{entry.Key} is empty in pt-BR."));
    }

    [Fact]
    public void Translations_keep_the_same_placeholders_as_the_english_text()
    {
        var english = Read("SharedResource.resx");
        var portuguese = Read("SharedResource.pt-BR.resx");

        Assert.All(english, entry => Assert.Equal(
            Placeholders(entry.Value),
            Placeholders(portuguese.GetValueOrDefault(entry.Key, string.Empty))));
    }

    [Fact]
    public void No_brazilian_portuguese_text_uses_european_portuguese_wording()
    {
        var pattern = new Regex(@"\b(" + string.Join('|', EuropeanPortuguese) + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var found = Read("SharedResource.pt-BR.resx")
            .Where(entry => pattern.IsMatch(entry.Value))
            .Select(entry => $"{entry.Key}: {entry.Value}")
            .ToList();

        Assert.Empty(found);
    }

    [Theory]
    [InlineData("Descarregue o original.")]
    [InlineData("Descarregar o original")]
    [InlineData("Toque no ecrã")]
    [InlineData("o ficheiro")]
    [InlineData("A restabelecer a ligação…")]
    [InlineData("Câmara")]
    public void The_european_portuguese_check_catches_the_known_words(string text)
    {
        var pattern = new Regex(@"\b(" + string.Join('|', EuropeanPortuguese) + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Assert.Matches(pattern, text);
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal);

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(Path.Combine(SourceTree.WebProject, "Resources", file))
            .Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty);

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();
}

/// <summary>The repository's source files, found from the test's output folder.</summary>
internal static class SourceTree
{
    public static string Root { get; } = FindRoot();

    public static string WebProject => Path.Combine(Root, "src", "Sepiabox.Web");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sepiabox.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Sepiabox.slnx not found above " + AppContext.BaseDirectory);
    }
}
