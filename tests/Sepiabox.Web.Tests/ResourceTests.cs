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
    // partilhar (compartilhar), definições (configurações), gerir (gerenciar), equipa (equipe),
    // contacto (contato), "correu mal" (deu errado), and the continuous "a" + infinitive (carregando).
    // The continuous form is matched only at the start of a text or after a form of "estar", so a Brazilian
    // "a" + infinitive ("começar a enviar", "para a enviar") is not flagged.
    private static readonly string[] EuropeanPortuguese =
    [
        @"descarreg\w*", @"ecr[ãa]s?", @"ficheiros?", @"log[óo]tipos?", @"liga[çc][ãa]o", @"liga[çc][õo]es",
        @"c[âa]maras?", @"utilizador\w*", @"telem[óo]ve(l|is)", @"registos?", @"palavra-passe",
        @"partilh\w*", @"defini[çc][õo]es", @"gerir", @"equipas?", @"contactos?", @"correu mal",
        @"(est[áa]|est[ãa]o|estou|estar|estamos)\s+a\s+\w+(ar|er|ir)",
        @"^A\s+\w+(ar|er|ir)\b(?=.*(…|\.\.\.)$)",
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
        var pattern = Pattern();

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
    [InlineData("A carregar…")]
    [InlineData("O vídeo está a carregar")]
    [InlineData("Estão a processar as fotos")]
    [InlineData("Partilhar o álbum")]
    [InlineData("Definições")]
    [InlineData("Peça a quem gere a biblioteca para gerir o acesso")]
    [InlineData("A equipa")]
    [InlineData("Os contactos")]
    public void The_european_portuguese_check_catches_the_known_words(string text)
    {
        var pattern = Pattern();

        Assert.Matches(pattern, text);
    }

    [Theory]
    [InlineData("Ainda não há fotos. Use sepiabox-ingest para enviar as do HD externo.")]
    [InlineData("Comece a enviar as fotos.")]
    [InlineData("A entrada ainda não está configurada.")]
    [InlineData("A sessão foi pausada pelo servidor.")]
    [InlineData("Compartilhar com a equipe os contatos")]
    public void The_european_portuguese_check_leaves_brazilian_text_alone(string text)
    {
        Assert.DoesNotMatch(Pattern(), text);
    }

    private static Regex Pattern() =>
        new(@"\b(" + string.Join('|', EuropeanPortuguese) + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
