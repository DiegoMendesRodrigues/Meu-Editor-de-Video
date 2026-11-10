using System.IO;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Infrastructure.Cache;
using MeuEditorDeVideo.Infrastructure.Paths;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Infrastructure;

public sealed class CacheMaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly string _media;
    private readonly string _mattes;

    public CacheMaintenanceTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "r"), Path.Combine(_root, "l"));
        _media = Path.Combine(_paths.Cache, "media");
        _mattes = Path.Combine(_paths.Cache, "mattes");
    }

    private CacheMaintenance Create() => new(_paths, NullLogger<CacheMaintenance>.Instance);

    /// <summary>Cria um arquivo com tamanho e datas (escrita e acesso) explícitas, <paramref name="idade"/> atrás de agora.</summary>
    private static void Arquivo(string pasta, string nome, int bytes, TimeSpan idade)
    {
        Directory.CreateDirectory(pasta);
        var arquivo = Path.Combine(pasta, nome);
        File.WriteAllBytes(arquivo, new byte[bytes]);
        var quando = DateTime.UtcNow - idade;
        File.SetLastWriteTimeUtc(arquivo, quando);
        File.SetLastAccessTimeUtc(arquivo, quando);
    }

    /// <summary>Cria uma entrada (subpasta de uma área) com um único arquivo.</summary>
    private static string Entrada(string area, string nome, int bytes, TimeSpan idade, string arquivo = "dados.bin")
    {
        var pasta = Path.Combine(area, nome);
        Arquivo(pasta, arquivo, bytes, idade);
        return pasta;
    }

    [Fact]
    public void Mede_as_duas_areas_com_bytes_e_entradas()
    {
        Entrada(_media, "k1", 100, TimeSpan.FromDays(1));
        Entrada(_media, "k2", 50, TimeSpan.FromDays(1));
        File.WriteAllBytes(Path.Combine(_media, "solto.bin"), new byte[999]); // arquivo direto na área: ignorado
        Entrada(_mattes, "7", 300, TimeSpan.FromHours(1), "0001.png");

        var usos = Create().Measure();

        Assert.Equal(2, usos.Count);
        var midia = usos.Single(u => u.Directory == _media);
        Assert.Equal("Miniaturas, formas de onda e proxies", midia.Area);
        Assert.Equal(150, midia.Bytes);
        Assert.Equal(2, midia.Entries);
        var mascaras = usos.Single(u => u.Directory == _mattes);
        Assert.Equal("Máscaras de IA", mascaras.Area);
        Assert.Equal(300, mascaras.Bytes);
        Assert.Equal(1, mascaras.Entries);
    }

    [Fact]
    public void Pastas_inexistentes_medem_zero()
    {
        var usos = Create().Measure();

        Assert.Equal(2, usos.Count);
        Assert.All(usos, u =>
        {
            Assert.Equal(0, u.Bytes);
            Assert.Equal(0, u.Entries);
        });
    }

    [Fact]
    public void Trim_remove_as_entradas_menos_usadas_das_duas_areas_ate_caber_no_limite()
    {
        var m1 = Entrada(_mattes, "1", 200, TimeSpan.FromDays(5));
        var k1 = Entrada(_media, "k1", 100, TimeSpan.FromDays(4));
        var k2 = Entrada(_media, "k2", 100, TimeSpan.FromDays(3));
        var m2 = Entrada(_mattes, "2", 100, TimeSpan.FromDays(1));
        var k3 = Entrada(_media, "k3", 100, TimeSpan.FromHours(1));

        var liberados = Create().Trim(350);

        Assert.Equal(300, liberados);
        Assert.False(Directory.Exists(m1));
        Assert.False(Directory.Exists(k1));
        Assert.True(Directory.Exists(k2));
        Assert.True(Directory.Exists(m2));
        Assert.True(Directory.Exists(k3));
    }

    [Fact]
    public void Trim_com_total_abaixo_do_limite_nao_apaga_nada()
    {
        var k1 = Entrada(_media, "k1", 100, TimeSpan.FromDays(9));

        var liberados = Create().Trim(100);

        Assert.Equal(0, liberados);
        Assert.True(File.Exists(Path.Combine(k1, "dados.bin")));
    }

    [Fact]
    public void Entrada_usada_ha_menos_de_dois_minutos_nao_e_removida()
    {
        var recente = Entrada(_media, "recente", 100, TimeSpan.FromSeconds(30));
        var antiga = Entrada(_media, "antiga", 100, TimeSpan.FromDays(1));

        var liberados = Create().Trim(0);

        Assert.Equal(100, liberados);
        Assert.False(Directory.Exists(antiga));
        Assert.True(File.Exists(Path.Combine(recente, "dados.bin")));
    }

    [Fact]
    public void Arquivo_bloqueado_nao_impede_o_trim_e_os_demais_sao_removidos()
    {
        var pasta = Path.Combine(_media, "k1");
        Arquivo(pasta, "livre.bin", 100, TimeSpan.FromDays(1));
        Arquivo(pasta, "travado.bin", 200, TimeSpan.FromDays(1));
        using var trava = new FileStream(Path.Combine(pasta, "travado.bin"), FileMode.Open, FileAccess.Read, FileShare.None);

        var liberados = Create().Trim(0);

        Assert.Equal(100, liberados);
        Assert.False(File.Exists(Path.Combine(pasta, "livre.bin")));
        Assert.True(File.Exists(Path.Combine(pasta, "travado.bin")));
    }

    [Fact]
    public void Arquivo_bloqueado_nao_impede_o_clear_e_os_demais_sao_removidos()
    {
        var pasta = Path.Combine(_media, "k1");
        Arquivo(pasta, "livre.bin", 100, TimeSpan.FromDays(1));
        Arquivo(pasta, "travado.bin", 200, TimeSpan.FromDays(1));
        using var trava = new FileStream(Path.Combine(pasta, "travado.bin"), FileMode.Open, FileAccess.Read, FileShare.None);

        var liberados = Create().Clear();

        Assert.Equal(100, liberados);
        Assert.False(File.Exists(Path.Combine(pasta, "livre.bin")));
        Assert.True(File.Exists(Path.Combine(pasta, "travado.bin")));
    }

    [Fact]
    public void Arquivos_em_projects_nunca_sao_removidos()
    {
        var projetos = Path.Combine(_paths.Cache, "projects", "p1");
        Arquivo(projetos, "projeto.med", 100, TimeSpan.FromDays(30));
        Entrada(_media, "k1", 100, TimeSpan.FromDays(1));
        var sut = Create();

        sut.Trim(0);
        Assert.True(File.Exists(Path.Combine(projetos, "projeto.med")));

        sut.Clear();
        Assert.True(File.Exists(Path.Combine(projetos, "projeto.med")));
    }

    [Fact]
    public void Clear_remove_tudo_nas_duas_areas_e_devolve_os_bytes()
    {
        Entrada(_media, "k1", 100, TimeSpan.FromDays(2));
        Entrada(_media, "k2", 40, TimeSpan.FromSeconds(10)); // uso recente também é apagado pelo Clear
        Entrada(_mattes, "1", 60, TimeSpan.FromDays(1), "0001.png");
        var sut = Create();

        var liberados = sut.Clear();

        Assert.Equal(200, liberados);
        Assert.All(sut.Measure(), u =>
        {
            Assert.Equal(0, u.Bytes);
            Assert.Equal(0, u.Entries);
        });
        Assert.False(Directory.Exists(Path.Combine(_media, "k1")));
    }

    [Fact]
    public void Limite_negativo_lanca_excecao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().Trim(-1));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
