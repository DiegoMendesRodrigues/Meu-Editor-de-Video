using MeuEditorDeVideo.Presentation.Input;
using MeuEditorDeVideo.Desktop.Services;
using SharpHook.Data;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Atalho global (SharpHook): mapeamento de teclas e escolha da implementação por sistema/ambiente.</summary>
public class GlobalHotkeyTests
{
    [Fact]
    public void F10_vira_VcF10()
    {
        Assert.Equal(KeyCode.VcF10, SharpHookGlobalHotkey.ToKeyCode(MedKey.F10));
    }

    [Fact]
    public void Todas_as_teclas_de_funcao_sao_mapeadas()
    {
        for (var i = 1; i <= 24; i++)
        {
            var key = Enum.Parse<MedKey>("F" + i);
            Assert.Equal(Enum.Parse<KeyCode>("VcF" + i), SharpHookGlobalHotkey.ToKeyCode(key));
        }
    }

    [Fact]
    public void Letras_digitos_e_teclas_comuns_sao_mapeados()
    {
        Assert.Equal(KeyCode.VcA, SharpHookGlobalHotkey.ToKeyCode(MedKey.A));
        Assert.Equal(KeyCode.VcZ, SharpHookGlobalHotkey.ToKeyCode(MedKey.Z));
        Assert.Equal(KeyCode.Vc0, SharpHookGlobalHotkey.ToKeyCode(MedKey.D0));
        Assert.Equal(KeyCode.Vc9, SharpHookGlobalHotkey.ToKeyCode(MedKey.D9));
        Assert.Equal(KeyCode.VcEscape, SharpHookGlobalHotkey.ToKeyCode(MedKey.Escape));
        Assert.Equal(KeyCode.VcSpace, SharpHookGlobalHotkey.ToKeyCode(MedKey.Space));
        Assert.Null(SharpHookGlobalHotkey.ToKeyCode(MedKey.None));
    }

    private static Func<string, string?> Env(params (string, string)[] values) =>
        name => values.Where(v => v.Item1 == name).Select(v => v.Item2).FirstOrDefault();

    [Fact]
    public void Windows_e_macOS_sempre_tem_suporte()
    {
        Assert.Equal(GlobalHotkeyUnavailableReason.None,
            GlobalHotkeySupport.Evaluate(isLinux: false, Env(("XDG_SESSION_TYPE", "wayland"))));
    }

    [Fact]
    public void Linux_com_sessao_Wayland_nao_tem_suporte()
    {
        Assert.Equal(GlobalHotkeyUnavailableReason.Wayland,
            GlobalHotkeySupport.Evaluate(true, Env(("XDG_SESSION_TYPE", "wayland"), ("DISPLAY", ":0"))));
        Assert.Equal(GlobalHotkeyUnavailableReason.Wayland,
            GlobalHotkeySupport.Evaluate(true, Env(("WAYLAND_DISPLAY", "wayland-0"))));
    }

    [Fact]
    public void Linux_com_X11_tem_suporte()
    {
        Assert.Equal(GlobalHotkeyUnavailableReason.None,
            GlobalHotkeySupport.Evaluate(true, Env(("XDG_SESSION_TYPE", "x11"), ("DISPLAY", ":0"))));
        Assert.Equal(GlobalHotkeyUnavailableReason.None,
            GlobalHotkeySupport.Evaluate(true, Env(("DISPLAY", ":0"), ("WAYLAND_DISPLAY", "wayland-0"))));
    }

    [Fact]
    public void Registro_de_tecla_sem_mapeamento_nao_inicia_o_gancho()
    {
        using var hotkey = new SharpHookGlobalHotkey();
        hotkey.Register(MedKey.None, () => { }).Dispose();
        Assert.True(hotkey.IsGlobal);
    }
}
