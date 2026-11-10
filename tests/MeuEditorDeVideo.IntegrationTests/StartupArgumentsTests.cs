using System.IO;
using MeuEditorDeVideo.App.Services;

namespace MeuEditorDeVideo.IntegrationTests;

/// <summary>Argumentos de linha de comando: caminho do .med aberto pelo Windows (associação de arquivo).</summary>
public sealed class StartupArgumentsTests : IDisposable
{
    private const string Obra = @"C:\Projetos\obra.med";
    private const string Outra = @"C:\Projetos\outra.med";

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "med-startup-" + Guid.NewGuid().ToString("N")[..8]);

    private static Func<string, bool> Existentes(params string[] caminhos) =>
        p => caminhos.Contains(p, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Retorna_o_caminho_completo_do_primeiro_med_existente()
    {
        var resultado = StartupArguments.ProjectPath([@"C:\Projetos\rascunho.txt", Obra, Outra], Existentes(Obra, Outra));

        Assert.Equal(Obra, resultado);
    }

    [Fact]
    public void Ignora_med_inexistente_e_usa_o_proximo_existente()
    {
        var resultado = StartupArguments.ProjectPath([@"C:\Projetos\apagado.med", Outra], Existentes(Outra));

        Assert.Equal(Outra, resultado);
    }

    [Fact]
    public void Ignora_arquivos_que_nao_sao_med_mesmo_existindo()
    {
        var resultado = StartupArguments.ProjectPath([@"C:\Midias\clipe.mp4", @"C:\Textos\notas.med.txt"],
            Existentes(@"C:\Midias\clipe.mp4", @"C:\Textos\notas.med.txt"));

        Assert.Null(resultado);
    }

    [Fact]
    public void Ignora_argumentos_que_comecam_com_hifen_ou_barra()
    {
        var resultado = StartupArguments.ProjectPath(["-open:" + Obra, "/abrir:" + Outra, "--debug.med"], p => true);

        Assert.Null(resultado);
    }

    [Fact]
    public void Extensao_e_case_insensitive()
    {
        var resultado = StartupArguments.ProjectPath([@"C:\Projetos\OBRA.MED"], Existentes(@"C:\Projetos\OBRA.MED"));

        Assert.Equal(@"C:\Projetos\OBRA.MED", resultado);
    }

    [Fact]
    public void Remove_aspas_do_caminho_e_espacos_nas_bordas()
    {
        var resultado = StartupArguments.ProjectPath(["  \"" + Obra + "\"  "], Existentes(Obra));

        Assert.Equal(Obra, resultado);
    }

    [Fact]
    public void Caminho_relativo_e_devolvido_como_completo()
    {
        var resultado = StartupArguments.ProjectPath(["obra.med"], p => true);

        Assert.Equal(Path.GetFullPath("obra.med"), resultado);
    }

    [Fact]
    public void Lista_vazia_ou_com_textos_vazios_devolve_nulo()
    {
        Assert.Null(StartupArguments.ProjectPath([], p => true));
        Assert.Null(StartupArguments.ProjectPath(["", "   "], p => true));
    }

    [Fact]
    public void Sem_funcao_de_existencia_usa_o_sistema_de_arquivos()
    {
        Directory.CreateDirectory(_pasta);
        var existente = Path.Combine(_pasta, "projeto de teste.med");
        File.WriteAllText(existente, "conteúdo qualquer");

        Assert.Equal(existente, StartupArguments.ProjectPath([existente]));
        Assert.Null(StartupArguments.ProjectPath([Path.Combine(_pasta, "nao-existe.med")]));
    }

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
