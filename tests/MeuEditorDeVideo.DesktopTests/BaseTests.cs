using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using MeuEditorDeVideo.Desktop;
using MeuEditorDeVideo.Desktop.Views.Dialogs;
using MeuEditorDeVideo.DesktopTests.Support;
using MeuEditorDeVideo.Presentation.Input;
using MeuEditorDeVideo.Presentation.Services;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Base do aplicativo Avalonia: tema, paleta, fonte de ícones, caixa de mensagem e teclas.</summary>
public class BaseTests
{
    [Fact]
    public void Paleta_acompanha_a_variante_do_tema()
    {
        Ui.Run(() =>
        {
            var app = Avalonia.Application.Current!;
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Assert.True(app.TryGetResource("Med.Background", ThemeVariant.Dark, out var dark));
            Assert.True(app.TryGetResource("Med.Background", ThemeVariant.Light, out var light));
            Assert.Equal(Color.Parse("#0C0C0F"), ((ISolidColorBrush)dark!).Color);
            Assert.Equal(Color.Parse("#CFCFD6"), ((ISolidColorBrush)light!).Color);
        });
    }

    [Fact]
    public void Fonte_de_icones_tem_os_pontos_de_codigo_da_Segoe()
    {
        Ui.Run(() =>
        {
            Assert.True(Avalonia.Application.Current!.TryGetResource("Med.IconFont", null, out var value));
            var typeface = new Typeface((FontFamily)value!);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs), "fonte Med Icons não carregou");
            Assert.Equal("Med Icons", glyphs!.FamilyName);
            foreach (var cp in new[] { 0x25B3 }.Concat(PrivateUseCodepointsInSources()))
                Assert.True(glyphs.CharacterToGlyphMap.TryGetGlyph(cp, out var g) && g != 0, $"sem glifo U+{cp:X4}");
        });
    }

    /// <summary>
    /// Ícones da área de uso privado citados no código da interface (.axaml com &#xE768;, ou o caractere literal nos
    /// ViewModels), para que nenhum ícone usado fique sem glifo na fonte gerada.
    /// </summary>
    private static IEnumerable<int> PrivateUseCodepointsInSources()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MeuEditorDeVideo.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var found = new SortedSet<int>();
        foreach (var project in new[] { "MeuEditorDeVideo.Desktop", "MeuEditorDeVideo.Presentation" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root!.FullName, "src", project), "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (!file.EndsWith(".cs", StringComparison.Ordinal) && !file.EndsWith(".axaml", StringComparison.Ordinal)) continue;
                var text = File.ReadAllText(file);
                foreach (var ch in text)
                    if (ch is >= '\uE000' and <= '\uF8FF') found.Add(ch);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "&#x([EeFf][0-9A-Fa-f]{3});"))
                    found.Add(Convert.ToInt32(m.Groups[1].Value, 16));
            }
        }
        Assert.NotEmpty(found);
        return found;
    }

    [Fact]
    public void Caixa_de_mensagem_desenha_texto_e_botoes()
    {
        var path = Ui.Run(() =>
        {
            var window = new MessageWindow("Salvar", "Deseja salvar as alterações em \"Praia\"?", MessageKind.Warning,
                ["Salvar", "Não salvar", "Cancelar"], 0, 2);
            var file = Ui.Capture(window, "mensagem");
            Assert.True(window.ClientSize.Width >= 360, $"largura {window.ClientSize.Width} x {window.ClientSize.Height}, bounds {window.Bounds}");
            window.Close();
            return file;
        });
        Assert.True(new FileInfo(path).Length > 1000, path);
    }

    [Theory]
    [InlineData(MedModifiers.Control, false, Avalonia.Input.KeyModifiers.Control)]
    [InlineData(MedModifiers.Control, true, Avalonia.Input.KeyModifiers.Meta)]
    [InlineData(MedModifiers.Control | MedModifiers.Shift, true, Avalonia.Input.KeyModifiers.Meta | Avalonia.Input.KeyModifiers.Shift)]
    [InlineData(MedModifiers.Alt, true, Avalonia.Input.KeyModifiers.Alt)]
    public void Cmd_do_macOS_faz_o_papel_do_Ctrl(MedModifiers med, bool mac, Avalonia.Input.KeyModifiers avalonia)
    {
        Assert.Equal(avalonia, KeyMapping.ToAvalonia(med, mac));
        Assert.Equal(med, KeyMapping.ToMed(avalonia, mac));
    }

    [Fact]
    public void Teclas_do_Avalonia_tem_os_mesmos_nomes_e_valores_de_MedKey()
    {
        foreach (var name in Enum.GetNames<MedKey>())
        {
            Assert.True(Enum.TryParse<Avalonia.Input.Key>(name, out var key), $"Avalonia sem {name}");
            Assert.Equal((int)Enum.Parse<MedKey>(name), (int)key);
        }
    }

    [Theory]
    [InlineData("Ctrl+OemPlus", Avalonia.Input.Key.OemPlus, Avalonia.Input.KeyModifiers.Control)]
    [InlineData("Ctrl+Shift+Z", Avalonia.Input.Key.Z, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift)]
    [InlineData("Space", Avalonia.Input.Key.Space, Avalonia.Input.KeyModifiers.None)]
    [InlineData("Alt+D1", Avalonia.Input.Key.D1, Avalonia.Input.KeyModifiers.Alt)]
    public void Texto_gravado_vira_a_mesma_tecla_do_Avalonia(string text, Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        // Os atalhos gravados no settings.json ("Ctrl+OemPlus") continuam válidos (fora do macOS, onde o Ctrl vira Cmd).
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(key, chord.Key.ToAvalonia());
        Assert.Equal(modifiers, KeyMapping.ToAvalonia(chord.Modifiers, mac: false));
        Assert.Equal(text, chord.ToString());
    }
}
