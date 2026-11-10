using System.Windows.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>Atalhos editáveis (RF212).</summary>
public sealed class ShortcutServiceTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();

        public event EventHandler<AppSettings>? Changed;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Changed?.Invoke(this, settings);
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("Ctrl+Shift+S", Key.S, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("Space", Key.Space, ModifierKeys.None)]
    [InlineData("ctrl+oemplus", Key.OemPlus, ModifierKeys.Control)]
    [InlineData("Alt+F4", Key.F4, ModifierKeys.Alt)]
    public void Combinacao_e_lida_do_texto(string text, Key key, ModifierKeys mods)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(key, mods), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Win+S")]
    [InlineData("Ctrl+LeftShift")]
    [InlineData("Ctrl+NaoExiste")]
    public void Texto_invalido_e_rejeitado(string text) => Assert.False(KeyChord.TryParse(text, out _));

    [Fact]
    public void Formato_salvo_e_exibicao_em_portugues()
    {
        var chord = new KeyChord(Key.Left, ModifierKeys.Shift);
        Assert.Equal("Shift+Left", chord.ToString());
        Assert.Equal("Shift+←", chord.Display);
        Assert.Equal("Espaço", new KeyChord(Key.Space, ModifierKeys.None).Display);
        Assert.Equal("Ctrl+,", new KeyChord(Key.OemComma, ModifierKeys.Control).Display);
        Assert.Equal("Ctrl+1", new KeyChord(Key.D1, ModifierKeys.Control).Display);
    }

    [Fact]
    public void Formato_salvo_volta_para_a_mesma_combinacao()
    {
        foreach (var action in ShortcutCatalog.All)
            foreach (var text in action.Defaults)
            {
                Assert.True(KeyChord.TryParse(text, out var chord), text);
                Assert.Equal(text, chord.ToString());
            }
    }

    [Fact]
    public void Padroes_nao_tem_conflitos_e_ids_sao_unicos()
    {
        Assert.Empty(ShortcutService.Conflicts(ShortcutService.Resolve(new Dictionary<string, string>())));
        Assert.Equal(ShortcutCatalog.All.Count, ShortcutCatalog.All.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void Padroes_respondem_as_teclas()
    {
        var service = new ShortcutService();
        Assert.Equal(ShortcutCatalog.Save, service.Find(Key.S, ModifierKeys.Control));
        Assert.Equal(ShortcutCatalog.Split, service.Find(Key.S, ModifierKeys.None));
        Assert.Equal(ShortcutCatalog.Redo, service.Find(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal(ShortcutCatalog.PlayPause, service.Find(Key.Space, ModifierKeys.None));
        Assert.Null(service.Find(Key.Q, ModifierKeys.Control));
        Assert.Equal("Ctrl+Y", service.Display(ShortcutCatalog.Redo));
    }

    [Fact]
    public void Alteracao_do_usuario_substitui_o_padrao()
    {
        var map = ShortcutService.Resolve(new Dictionary<string, string> { [ShortcutCatalog.Split] = "Ctrl+B" });
        Assert.Equal([new KeyChord(Key.B, ModifierKeys.Control)], map[ShortcutCatalog.Split]);
    }

    [Fact]
    public void Texto_vazio_remove_o_atalho()
    {
        var map = ShortcutService.Resolve(new Dictionary<string, string> { [ShortcutCatalog.Marker] = "" });
        Assert.Empty(map[ShortcutCatalog.Marker]);
    }

    [Fact]
    public void Conflito_e_detectado()
    {
        var map = ShortcutService.Resolve(new Dictionary<string, string> { [ShortcutCatalog.Marker] = "Ctrl+S" });
        var conflict = Assert.Single(ShortcutService.Conflicts(map));
        Assert.Equal(new KeyChord(Key.S, ModifierKeys.Control), conflict.Key);
        Assert.Equal([ShortcutCatalog.Marker, ShortcutCatalog.Save], conflict.Value.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Servico_acompanha_as_configuracoes_salvas()
    {
        var settings = new FakeSettings();
        var service = new ShortcutService(settings);
        var changed = 0;
        service.Changed += (_, _) => changed++;

        await settings.SaveAsync(settings.Current with
        {
            Shortcuts = new Dictionary<string, string> { [ShortcutCatalog.Marker] = "Ctrl+M" },
        });

        Assert.Equal(1, changed);
        Assert.Equal(ShortcutCatalog.Marker, service.Find(Key.M, ModifierKeys.Control));
        Assert.Null(service.Find(Key.M, ModifierKeys.None));
        Assert.Equal("Ctrl+M", service.Display(ShortcutCatalog.Marker));
    }
}
