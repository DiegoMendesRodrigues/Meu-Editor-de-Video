using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.App.Localization;
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
