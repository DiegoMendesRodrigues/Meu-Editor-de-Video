using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.Presentation.Localization;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>O tradutor é estático: os testes que o usam ficam na mesma coleção, sem paralelismo entre si.</summary>
[CollectionDefinition("Translator", DisableParallelization = true)]
public sealed class TranslatorCollection;

[Collection("Translator")]
public sealed partial class TranslatorTests : IDisposable
{
    public void Dispose() => Translator.Reset();

    [Fact]
    public void Sem_traducao_devolve_o_texto_original()
    {
        Translator.Reset();
        Assert.Equal("Salvar", Translator.T("Salvar"));
        Assert.False(Translator.IsActive);
    }

    [Fact]
    public void Traduz_texto_exato_e_com_espacos_nas_pontas()
    {
        Translator.Use(new Dictionary<string, string> { ["Salvar"] = "Save" });
        Assert.Equal("Save", Translator.T("Salvar"));
        Assert.Equal("  Save ", Translator.T("  Salvar "));
        Assert.Equal("Desconhecido", Translator.T("Desconhecido"));
    }

    [Fact]
    public void Traduz_rotulo_com_dois_pontos_e_tecla_de_acesso()
    {
        Translator.Use(new Dictionary<string, string> { ["Idioma"] = "Language", ["Arquivo"] = "File" });
        Assert.Equal("Language:", Translator.T("Idioma:"));
        Assert.Equal("_File", Translator.T("_Arquivo"));
    }

    [Fact]
    public void Modelo_com_pouco_texto_fixo_so_aceita_numeros()
    {
        Translator.Use(new Dictionary<string, string> { ["D{0}"] = "R{0}" });
        Assert.Equal("R12", Translator.T("D12"));
        Assert.Equal("Default folders", Translator.T("Default folders"));
    }

    [Fact]
    public void Modelo_com_valores_traduz_tambem_os_argumentos()
    {
        Translator.Use(new Dictionary<string, string>
        {
            ["Projeto reenquadrado para {0}×{1} ({2} {3})"] = "Project reframed to {0}×{1} ({2} {3})",
            ["clipes"] = "clips",
        });
        Assert.Equal("Project reframed to 1080×1920 (2 clips)", Translator.T("Projeto reenquadrado para 1080×1920 (2 clipes)"));
    }

    [Fact]
    public void Formata_com_o_modelo_traduzido()
    {
        Translator.Use(new Dictionary<string, string> { ["Deseja salvar as alterações em \"{0}\"?"] = "Save changes to \"{0}\"?" });
        Assert.Equal("Save changes to \"Demo\"?", Translator.F("Deseja salvar as alterações em \"{0}\"?", "Demo"));
    }

    [Fact]
    public void Texto_de_varias_linhas_e_traduzido_linha_a_linha()
    {
        Translator.Use(new Dictionary<string, string> { ["Linha um."] = "Line one.", ["Linha dois."] = "Line two." });
        Assert.Equal("Line one.\n\nLine two.", Translator.T("Linha um.\n\nLinha dois."));
    }

    [Fact]
    public void Descricao_montada_com_o_rotulo_em_minusculas_e_traduzida_por_inteiro()
    {
        Translator.Use(new Dictionary<string, string>
        {
            ["Ajustar {0}"] = "Adjust {0}",
            ["Ajustar {0}: {1}"] = "Adjust {0}: {1}",
            ["Desfazer: {0} (Ctrl+Z)"] = "Undo: {0} (Ctrl+Z)",
            ["Remover {0}"] = "Remove {0}",
            ["Máscara {0}"] = "Mask {0}",
            ["Deslocamento X"] = "X offset",
            ["Vinheta"] = "Vignette",
            ["Intensidade"] = "Intensity",
        });
        Assert.Equal("Adjust X offset", Translator.T("Ajustar deslocamento x"));
        Assert.Equal("Adjust Vignette: intensity", Translator.T("Ajustar Vinheta: intensidade"));
        Assert.Equal("Vignette: intensity", Translator.T("Vinheta: intensidade"));
        Assert.Equal("Remove mask 2", Translator.T("Remover máscara 2"));
        Assert.Equal("Undo: Adjust intensity (Ctrl+Z)", Translator.T("Desfazer: Ajustar intensidade (Ctrl+Z)"));
    }

    [Fact]
    public void Nome_do_usuario_entre_aspas_nao_vira_rotulo()
    {
        Translator.Use(new Dictionary<string, string> { ["Importar \"{0}\""] = "Import \"{0}\"", ["Praia"] = "Beach" });
        Assert.Equal("Import \"praia\"", Translator.T("Importar \"praia\""));
        Assert.Equal("praia", Translator.T("praia"));
    }

    [Fact]
    public void Lista_de_problemas_e_traduzida_item_a_item()
    {
        Translator.Use(new Dictionary<string, string> { ["Problemas:"] = "Problems:", ["Trilha vazia."] = "Empty track." });
        Assert.Equal("Problems:\n• Empty track.", Translator.T("Problemas:\n• Trilha vazia."));
    }

    public static TheoryData<string> Languages => ["en", "es", "fr", "de", "pt-PT"];

    /// <summary>Cada linha dos .tsv aponta para um texto de origem e mantém os mesmos marcadores {n}.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Arquivos_de_traducao_correspondem_a_origem(string language)
    {
        var folder = FindI18nFolder();
        var sources = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(folder, "origem.json")))!;
        var byId = sources.GroupBy(Translator.IdOf).ToDictionary(g => g.Key, g => g.First());
        var lines = File.ReadAllLines(Path.Combine(folder, language + ".tsv"));
        Assert.True(lines.Length > 500, $"{language}.tsv tem só {lines.Length} linhas.");
        foreach (var line in lines)
        {
            var tab = line.IndexOf('\t');
            Assert.True(tab == 8, $"Linha inválida em {language}.tsv: {line}");
            Assert.True(byId.TryGetValue(line[..tab], out var source), $"Id sem origem em {language}.tsv: {line}");
            var target = line[(tab + 1)..].Replace("\\\\", "\\");
            Assert.Equal(Markers(source), Markers(target));
        }

        Translator.Load(language, folder);
        Assert.True(Translator.IsActive);
        Assert.Equal(language, Translator.Language);
    }

    /// <summary>
    /// Texto novo precisa de tradução. O que fica igual ao português de propósito (nomes, siglas, atalhos, identificadores)
    /// entra em <c>textos-sem-traducao.json</c>: em "todos" ou na lista do idioma.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    public void Todo_texto_de_origem_tem_traducao_ou_excecao(string language)
    {
        var folder = Path.Combine(FindRepository(), "resources", "i18n");
        var sources = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(folder, "origem.json")))!;
        var translated = File.ReadAllLines(Path.Combine(folder, language + ".tsv")).Select(l => l[..8]).ToHashSet();
        var exceptions = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(Path.Combine(
            FindRepository(), "tests", "MeuEditorDeVideo.UnitTests", "Application", "textos-sem-traducao.json")))!;
        var allowed = exceptions["todos"].Concat(exceptions[language]).ToHashSet();
        var missing = sources.Where(s => !translated.Contains(Translator.IdOf(s)) && !allowed.Contains(s)).ToList();
        Assert.True(missing.Count == 0, $"Sem tradução em {language}.tsv:\n" + string.Join('\n', missing));
        var stale = allowed.Except(sources).ToList();
        Assert.True(stale.Count == 0, "Exceções que saíram de origem.json:\n" + string.Join('\n', stale));
    }

    /// <summary>A mensagem já montada com os valores encontra o seu modelo e sai traduzida.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Mensagem_montada_sai_traduzida(string language)
    {
        var folder = Path.Combine(FindRepository(), "resources", "i18n");
        var sources = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(folder, "origem.json")))!;
        var translated = File.ReadAllLines(Path.Combine(folder, language + ".tsv")).Select(l => l[..8]).ToHashSet();
        Translator.Load(language, folder);
        var untranslated = new List<string>();
        foreach (var source in sources.Where(s => MarkerRegex().IsMatch(s) && translated.Contains(Translator.IdOf(s))))
        {
            var shown = MarkerRegex().Replace(source, "7");
            if (shown.Count(char.IsLetter) < 3) continue;
            if (Translator.T(shown) == shown && MarkerRegex().Replace(Translator.T(source), "7") != shown) untranslated.Add(source);
        }
        Assert.True(untranslated.Count == 0, $"Sem tradução ao montar ({language}):\n" + string.Join('\n', untranslated));
    }

    /// <summary>Termos só do Brasil que não podem voltar à interface nem ao manual de Portugal.</summary>
    [Fact]
    public void Portugues_de_Portugal_nao_usa_termos_do_Brasil()
    {
        var folder = Path.Combine(FindRepository(), "resources", "i18n");
        var sources = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(folder, "origem.json")))!;
        var target = File.ReadAllLines(Path.Combine(folder, "pt-PT.tsv")).ToDictionary(l => l[..8], l => l[9..]);
        // "**Outros arquivos:**" é conteúdo do LEIA-ME gerado dentro do pacote de modelo, não é interface.
        var shown = sources.Where(s => s != "**Outros arquivos:**").Select(s => target.GetValueOrDefault(Translator.IdOf(s), s))
            .Concat(File.ReadAllLines(Path.Combine(FindRepository(), "documentation", "MANUAL_DO_USUARIO.pt-PT.md"))
                .Select(l => NotProseRegex().Replace(l, "")));
        var found = shown.Where(t => BrazilianRegex().IsMatch(t)).ToList();
        Assert.True(found.Count == 0, "Termos do Brasil em pt-PT:\n" + string.Join('\n', found));
    }

    [GeneratedRegex(@"\(imagens/[^)]+\)|`[^`]+`")]
    private static partial Regex NotProseRegex();

    [GeneratedRegex(@"\b([Gg]erenci\w+|[Ss]eç(ão|ões)|[Aa]rquivos?|[Ss]alv(ar|e|o|amento)|[Uu]suários?|mouse|[Cc]âmeras?|[Mm]aquiagem|[Aa]plicativo|[Aa]bas?|[Ee]mbaixo|[Nn]otebooks|[Cc]ompartilh\w+|[Dd]etectad[oa]|[Íí]mã|[Cc]ontroles?|[Rr]egistro|[Ss]omente|na ecrã|[Aa]s ecrãs)\b")]
    private static partial Regex BrazilianRegex();

    private static string FindRepository()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "MeuEditorDeVideo.sln"))) dir = dir.Parent!;
        return dir.FullName;
    }

    private static string[] Markers(string text) =>
        [.. MarkerRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order()];

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex MarkerRegex();

    private static string FindI18nFolder()
    {
        var built = Path.Combine(AppContext.BaseDirectory, "i18n");
        if (File.Exists(Path.Combine(built, "origem.json"))) return built;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "resources", "i18n");
            if (File.Exists(Path.Combine(candidate, "origem.json"))) return candidate;
        }
        throw new DirectoryNotFoundException("resources\\i18n não encontrado.");
    }
}

public sealed class SupportedLanguagesTests
{
    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "pt-PT")]
    [InlineData("pt-AO", "pt-PT")]
    [InlineData("de-AT", "de")]
    [InlineData("es-MX", "es")]
    [InlineData("fr-CA", "fr")]
    [InlineData("en-GB", "en")]
    [InlineData("ja-JP", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void Idioma_do_Windows_vira_o_idioma_suportado_mais_proximo(string? culture, string expected) =>
        Assert.Equal(expected, SupportedLanguages.FromCulture(culture));

    [Fact]
    public void Seis_idiomas_suportados()
    {
        Assert.Equal(["de", "es", "fr", "en", "pt-BR", "pt-PT"], SupportedLanguages.All.Select(l => l.Code));
        Assert.False(SupportedLanguages.IsSupported("it"));
    }
}
