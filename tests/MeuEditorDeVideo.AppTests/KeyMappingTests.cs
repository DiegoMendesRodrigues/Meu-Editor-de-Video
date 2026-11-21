using System.Reflection;
using System.Windows.Input;
using MeuEditorDeVideo.App;
using MeuEditorDeVideo.Presentation.Input;
using MeuEditorDeVideo.Presentation.Services;
using NetArchTest.Rules;

namespace MeuEditorDeVideo.AppTests;

/// <summary>
/// As teclas do Presentation espelham as do WPF: a conversão é um cast e os nomes gravados no settings.json
/// ("Ctrl+OemPlus") continuam válidos.
/// </summary>
public class KeyMappingTests
{
    [Fact]
    public void Toda_tecla_do_WPF_tem_o_mesmo_nome_e_valor_em_MedKey()
    {
        foreach (var name in Enum.GetNames<Key>())
        {
            var wpf = Enum.Parse<Key>(name);
            Assert.True(Enum.TryParse<MedKey>(name, out var med), $"MedKey sem {name}");
            Assert.Equal((int)wpf, (int)med);
            Assert.Equal(med, wpf.ToMed());
            Assert.Equal(wpf, med.ToWpf());
        }
        Assert.Equal(Enum.GetNames<Key>().Length, Enum.GetNames<MedKey>().Length);
    }

    [Fact]
    public void Nome_principal_de_cada_valor_e_o_mesmo()
    {
        // Valores com sinônimos (Enter/Return, Capital/CapsLock…): o texto gravado usa o nome que ToString devolve.
        foreach (var value in Enum.GetValues<Key>().Distinct())
            Assert.Equal(value.ToString(), value.ToMed().ToString());
    }

    [Fact]
    public void Modificadores_tem_os_mesmos_valores()
    {
        foreach (var name in Enum.GetNames<ModifierKeys>())
            Assert.Equal((int)Enum.Parse<ModifierKeys>(name), (int)Enum.Parse<MedModifiers>(name));
        Assert.Equal(MedModifiers.Control | MedModifiers.Shift, (ModifierKeys.Control | ModifierKeys.Shift).ToMed());
    }

    [Theory]
    [InlineData("Ctrl+OemPlus", Key.OemPlus, ModifierKeys.Control)]
    [InlineData("Ctrl+Shift+Z", Key.Z, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("Space", Key.Space, ModifierKeys.None)]
    [InlineData("Alt+D1", Key.D1, ModifierKeys.Alt)]
    public void Texto_gravado_vira_a_mesma_tecla_do_WPF(string text, Key key, ModifierKeys modifiers)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(key, chord.Key.ToWpf());
        Assert.Equal(modifiers, chord.Modifiers.ToWpf());
        Assert.Equal(text, chord.ToString());
    }
}

/// <summary>Só o ModelDownloads usa a rede; o aplicativo WPF também não.</summary>
public class AppPrivacyTests
{
    [Fact]
    public void Aplicativo_nao_usa_APIs_de_rede()
    {
        var result = Types.InAssembly(typeof(KeyMapping).Assembly)
            .ShouldNot().HaveDependencyOnAny("System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets",
                "System.Net.WebClient", "System.Net.HttpWebRequest", "System.Net.Mail")
            .GetResult();
        Assert.True(result.IsSuccessful, "Uso de rede encontrado em: " + string.Join(", ", result.FailingTypeNames ?? []));
        Assert.Equal("MeuEditorDeVideo", typeof(KeyMapping).Assembly.GetName().Name);
        var refs = typeof(KeyMapping).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("System.Net.Http", refs);
        Assert.DoesNotContain("System.Net.Sockets", refs);
    }
}
