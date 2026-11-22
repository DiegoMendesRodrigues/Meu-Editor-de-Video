namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>
/// Atalho global (vale com outro programa em primeiro plano), usado para parar a gravação de tela com o aplicativo
/// minimizado. A implementação real (SharpHook) vem depois; por ora <see cref="GlobalHotkeys.Current"/> não registra nada
/// e a janela de gravação cai para a tecla da própria janela.
/// </summary>
public interface IGlobalHotkey
{
    /// <summary>True quando o atalho vale mesmo com a janela sem foco.</summary>
    bool IsGlobal { get; }

    /// <summary>Registra a tecla. Descarte o resultado para remover o atalho.</summary>
    /// <exception cref="InvalidOperationException">A tecla já está em uso por outro programa (mensagem para o usuário).</exception>
    IDisposable Register(MedKey key, Action pressed);
}

/// <summary>Implementação sem efeito: nenhum atalho global é registrado.</summary>
public sealed class NoGlobalHotkey : IGlobalHotkey
{
    public bool IsGlobal => false;

    public IDisposable Register(MedKey key, Action pressed) => new Registration();

    private sealed class Registration : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>Implementação em uso pelas janelas (troque no início do programa quando houver uma real).</summary>
public static class GlobalHotkeys
{
    public static IGlobalHotkey Current { get; set; } = new NoGlobalHotkey();
}
