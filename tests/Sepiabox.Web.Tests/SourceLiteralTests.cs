using System.Text.RegularExpressions;

namespace Sepiabox.Web.Tests;

/// <summary>
/// UI text lives only in the resource files (D-050): no Portuguese is left in the web's Razor components,
/// C# or scripts, and none of the Portuguese routes S-055 renamed (D-050).
/// </summary>
public sealed class SourceLiteralTests
{
    // The Portuguese text S-009 to S-011 wrote, as it appeared in the source.
    private static readonly string[] KnownLiterals =
    [
        "Entrar", "Sair", "Sem acesso", "Sem data", "Sem pré", "Foto", "Vídeo", "miniatura", "duração",
        "Descarreg", "descarreg", "Linha do tempo", "Câmara", "Dimensões", "Duração", "Tipo", "Onde está",
        "A carregar", "Ainda não há", "Não há mais", "Voltar ao início", "Mais recentes", "Mais antigas", "Páginas",
        "Ocorreu um erro", "Recarregar", "Fechar", "ligação", "Tentar novamente", "Retomar", "Algo correu mal",
        "Código do pedido", "Página não encontrada", "Esta página", "Esta conta", "Esta sessão", "Peça a quem",
        "outra conta", "só em desenvolvimento", "ainda não está configurada", "Não foi possível", "em processamento",
        "janeiro", "julho", "dezembro", "de 20",
        "fotos", "entrar", "sem-acesso", "sair", "pagina", "\"erro\"",
    ];

    // Letters Portuguese uses and English does not; none belongs in code, comments or markup outside resources.
    private static readonly Regex PortugueseLetters = new("[ãõçáéíóúâêôàÃÕÇÁÉÍÓÚÂÊÔÀ]", RegexOptions.CultureInvariant);

    public static TheoryData<string> SourceFiles() => [.. Files()];

    [Fact]
    public void The_check_finds_the_web_s_components_scripts_and_code()
    {
        var files = Files().ToList();

        Assert.Contains(Path.Combine("Components", "Pages", "Timeline.razor"), files);
        Assert.Contains(Path.Combine("Components", "Media", "VideoPlayer.razor"), files);
        Assert.Contains(Path.Combine("wwwroot", "video.js"), files);
        Assert.Contains(Path.Combine("Services", "MediaEndpoints.cs"), files);
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void Has_no_portuguese_text_outside_the_resources(string file)
    {
        var text = File.ReadAllText(Path.Combine(SourceTree.WebProject, file));

        var letters = PortugueseLetters.Matches(text).Select(m => m.Value).Distinct().ToList();
        var literals = KnownLiterals.Where(literal => text.Contains(literal, StringComparison.Ordinal)).ToList();

        Assert.Empty(letters);
        Assert.Empty(literals);
    }

    private static IEnumerable<string> Files() =>
        Directory.EnumerateFiles(SourceTree.WebProject, "*", SearchOption.AllDirectories)
            .Where(IsSource)
            .Select(f => Path.GetRelativePath(SourceTree.WebProject, f))
            .Order(StringComparer.Ordinal);

    private static bool IsSource(string path)
    {
        var relative = Path.GetRelativePath(SourceTree.WebProject, path);
        var top = relative.Split(Path.DirectorySeparatorChar)[0];
        return top is not ("bin" or "obj")
            && Path.GetExtension(path) is ".razor" or ".cs" or ".js" or ".cshtml";
    }
}
