using MeuEditorDeVideo.Presentation.Input;
using MeuEditorDeVideo.Presentation.Services;
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
    [InlineData("Ctrl+Shift+S", MedKey.S, MedModifiers.Control | MedModifiers.Shift)]
    [InlineData("Space", MedKey.Space, MedModifiers.None)]
    [InlineData("ctrl+oemplus", MedKey.OemPlus, MedModifiers.Control)]
    [InlineData("Alt+F4", MedKey.F4, MedModifiers.Alt)]
    public void Combinacao_e_lida_do_texto(string text, MedKey key, MedModifiers mods)
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
        var chord = new KeyChord(MedKey.Left, MedModifiers.Shift);
        Assert.Equal("Shift+Left", chord.ToString());
        Assert.Equal("Shift+←", chord.Display);
        Assert.Equal("Espaço", new KeyChord(MedKey.Space, MedModifiers.None).Display);
        Assert.Equal("Ctrl+,", new KeyChord(MedKey.OemComma, MedModifiers.Control).Display);
        Assert.Equal("Ctrl+1", new KeyChord(MedKey.D1, MedModifiers.Control).Display);
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
        Assert.Equal(ShortcutCatalog.Save, service.Find(MedKey.S, MedModifiers.Control));
        Assert.Equal(ShortcutCatalog.Split, service.Find(MedKey.S, MedModifiers.None));
        Assert.Equal(ShortcutCatalog.Redo, service.Find(MedKey.Z, MedModifiers.Control | MedModifiers.Shift));
        Assert.Equal(ShortcutCatalog.PlayPause, service.Find(MedKey.Space, MedModifiers.None));
        Assert.Null(service.Find(MedKey.Q, MedModifiers.Control));
        Assert.Equal("Ctrl+Y", service.Display(ShortcutCatalog.Redo));
    }

    [Fact]
    public void Alteracao_do_usuario_substitui_o_padrao()
    {
        var map = ShortcutService.Resolve(new Dictionary<string, string> { [ShortcutCatalog.Split] = "Ctrl+B" });
        Assert.Equal([new KeyChord(MedKey.B, MedModifiers.Control)], map[ShortcutCatalog.Split]);
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
        Assert.Equal(new KeyChord(MedKey.S, MedModifiers.Control), conflict.Key);
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
        Assert.Equal(ShortcutCatalog.Marker, service.Find(MedKey.M, MedModifiers.Control));
        Assert.Null(service.Find(MedKey.M, MedModifiers.None));
        Assert.Equal("Ctrl+M", service.Display(ShortcutCatalog.Marker));
    }
}
