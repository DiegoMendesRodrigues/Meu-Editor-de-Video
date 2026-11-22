using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;

namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>Por que o atalho global não está disponível (ou <see cref="None"/> quando está).</summary>
public enum GlobalHotkeyUnavailableReason
{
    None,
    Wayland,
}

/// <summary>Decisão pura (testável) sobre usar ou não o atalho global, a partir do sistema e das variáveis de ambiente.</summary>
public static class GlobalHotkeySupport
{
    /// <summary>
    /// A libuiohook precisa do X11: em sessão Wayland o atalho vale só com a janela em primeiro plano. No Windows e no
    /// macOS vale sempre (no macOS depende da permissão de Acessibilidade, descoberta ao iniciar o gancho).
    /// </summary>
    public static GlobalHotkeyUnavailableReason Evaluate(bool isLinux, Func<string, string?> getEnv)
    {
        if (!isLinux) return GlobalHotkeyUnavailableReason.None;
        var sessionType = getEnv("XDG_SESSION_TYPE");
        if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)) return GlobalHotkeyUnavailableReason.Wayland;
        if (string.IsNullOrEmpty(sessionType) && !string.IsNullOrEmpty(getEnv("WAYLAND_DISPLAY")) && string.IsNullOrEmpty(getEnv("DISPLAY")))
            return GlobalHotkeyUnavailableReason.Wayland;
        return GlobalHotkeyUnavailableReason.None;
    }

    /// <summary>Escolhe a implementação para o sistema atual: SharpHook, ou a sem efeito quando não há suporte.</summary>
    public static IGlobalHotkey Create(ILogger? logger)
    {
        var reason = Evaluate(OperatingSystem.IsLinux(), Environment.GetEnvironmentVariable);
        if (reason == GlobalHotkeyUnavailableReason.Wayland)
        {
            logger?.LogWarning("Sessão Wayland: o atalho global (F10 da gravação de tela) não está disponível; vale só com a janela em primeiro plano.");
            return new NoGlobalHotkey();
        }
        return new SharpHookGlobalHotkey(logger);
    }
}

/// <summary>
/// Atalho global com SharpHook (libuiohook). O gancho de teclado só é iniciado no primeiro <see cref="Register"/> e
/// encerrado quando o último atalho é removido. Se não puder iniciar (por exemplo, sem a permissão de Acessibilidade
/// no macOS), registra um aviso, passa a informar <see cref="IsGlobal"/> = false e os atalhos valem só com a janela.
/// </summary>
public sealed class SharpHookGlobalHotkey : IGlobalHotkey, IDisposable
{
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private SimpleGlobalHook? _hook;
    private bool _failed;
    private bool _disposed;

    public SharpHookGlobalHotkey(ILogger? logger = null) => _logger = logger;

    /// <summary>Código do SharpHook para a tecla; null quando não há mapeamento (F1–F24, A–Z, 0–9 e teclas de edição).</summary>
    public static KeyCode? ToKeyCode(MedKey key)
    {
        var name = key switch
        {
            >= MedKey.D0 and <= MedKey.D9 => "Vc" + (key - MedKey.D0),
            >= MedKey.A and <= MedKey.Z => "Vc" + key,
            >= MedKey.F1 and <= MedKey.F24 => "Vc" + key,
            MedKey.Escape => "VcEscape",
            MedKey.Space => "VcSpace",
            MedKey.Pause => "VcPause",
            MedKey.PrintScreen => "VcPrintScreen",
            MedKey.Return => "VcEnter",
            MedKey.Tab => "VcTab",
            MedKey.Back => "VcBackspace",
            MedKey.Insert => "VcInsert",
            MedKey.Delete => "VcDelete",
            MedKey.Home => "VcHome",
            MedKey.End => "VcEnd",
            MedKey.PageUp => "VcPageUp",
            MedKey.PageDown => "VcPageDown",
            _ => null,
        };
        return name is not null && Enum.TryParse<KeyCode>(name, out var code) ? code : null;
    }

    /// <summary>Verdadeiro enquanto o gancho não falhou (antes do primeiro registro ainda não se sabe).</summary>
    public bool IsGlobal
    {
        get
        {
            lock (_gate) return !_failed && !_disposed;
        }
    }

    public IDisposable Register(MedKey key, Action pressed)
    {
        var code = ToKeyCode(key);
        var entry = new Entry(this, code, pressed);
        lock (_gate)
        {
            if (_disposed || _failed || code is null) return entry;
            _entries.Add(entry);
            if (_hook is null)
            {
                _hook = StartHook();
                if (_hook is null)
                {
                    _entries.Clear();
                    _failed = true;
                }
            }
        }
        return entry;
    }

    private SimpleGlobalHook? StartHook()
    {
        SimpleGlobalHook? hook = null;
        try
        {
            hook = new SimpleGlobalHook();
            hook.KeyPressed += OnKeyPressed;
            var enabled = new TaskCompletionSource();
            hook.HookEnabled += (_, _) => enabled.TrySetResult();
            var run = hook.RunAsync(GlobalHookType.Keyboard);
            // Falhas ao iniciar (biblioteca ausente, permissão negada) chegam pela tarefa; o sucesso, pelo evento.
            Task.WhenAny(run, enabled.Task, Task.Delay(TimeSpan.FromSeconds(3))).GetAwaiter().GetResult();
            if (run.IsCompleted)
            {
                run.GetAwaiter().GetResult();
                throw new InvalidOperationException("O gancho de teclado terminou logo ao iniciar.");
            }
            if (!enabled.Task.IsCompleted) throw new TimeoutException("O gancho de teclado não iniciou a tempo.");
            return hook;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Não foi possível iniciar o atalho global (no macOS, conceda a permissão de Acessibilidade ao programa); o atalho vale só com a janela em primeiro plano.");
            try { hook?.Dispose(); } catch (Exception) { }
            return null;
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        Entry[] hit;
        lock (_gate) hit = _entries.Where(x => x.Code == e.Data.KeyCode).ToArray();
        foreach (var entry in hit)
            Dispatcher.UIThread.Post(entry.Pressed);
    }

    private void Remove(Entry entry)
    {
        SimpleGlobalHook? toStop;
        lock (_gate)
        {
            if (!_entries.Remove(entry) || _entries.Count > 0) return;
            toStop = _hook;
            _hook = null;
        }
        StopHook(toStop);
    }

    private void StopHook(SimpleGlobalHook? hook)
    {
        if (hook is null) return;
        try
        {
            hook.KeyPressed -= OnKeyPressed;
            hook.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Falha ao encerrar o atalho global.");
        }
    }

    public void Dispose()
    {
        SimpleGlobalHook? toStop;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _entries.Clear();
            toStop = _hook;
            _hook = null;
        }
        StopHook(toStop);
    }

    private sealed class Entry(SharpHookGlobalHotkey owner, KeyCode? code, Action pressed) : IDisposable
    {
        private int _disposed;

        public KeyCode? Code => code;

        public Action Pressed => pressed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Remove(this);
        }
    }
}
