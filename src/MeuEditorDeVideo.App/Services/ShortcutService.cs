using System.Windows.Input;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.App.Services;

/// <summary>Combinação de teclas (tecla + modificadores).</summary>
public readonly record struct KeyChord(Key Key, ModifierKeys Modifiers)
{
    /// <summary>Formato salvo nas configurações (nomes do WPF, invariável): "Ctrl+Shift+S", "Space", "Ctrl+OemPlus".</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Texto exibido ao usuário (pt-BR): "Ctrl+Shift+S", "Espaço", "Shift+←".</summary>
    public string Display
    {
        get
        {
            var parts = new List<string>(4);
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }
    }

    public static string KeyName(Key key) => key switch
    {
        Key.Space => "Espaço",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.Add => "+ (numérico)",
        Key.Subtract => "- (numérico)",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.PageUp => "Page Up",
        Key.PageDown => "Page Down",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString(),
    };

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                default: return false;
            }
        }
        if (!Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key) || !IsAssignable(key)) return false;
        chord = new KeyChord(key, mods);
        return true;
    }

    /// <summary>Teclas que não podem ser atalho sozinhas (modificadores, sistema).</summary>
    public static bool IsAssignable(Key key) => key is not (Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift
        or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.ImeProcessed
        or Key.DeadCharProcessed or Key.Tab or Key.Apps or Key.Capital or Key.NumLock or Key.Scroll);
}

/// <summary>Ação com atalho configurável (RF212).</summary>
/// <param name="Defaults">Combinações padrão (a primeira é a principal).</param>
public sealed record ShortcutAction(string Id, string Label, string Category, params string[] Defaults);

/// <summary>Ações com atalho e seus padrões.</summary>
public static class ShortcutCatalog
{
    public const string NewProject = "projeto.novo", OpenProject = "projeto.abrir", Save = "projeto.salvar", SaveAs = "projeto.salvarComo",
        Import = "midia.importar", Export = "projeto.exportar", Undo = "editar.desfazer", Redo = "editar.refazer", Settings = "app.configuracoes",
        PlayPause = "reproducao.reproduzir", FullScreen = "reproducao.telaCheia", StepBack = "reproducao.quadroAnterior",
        StepForward = "reproducao.proximoQuadro", StepBackSecond = "reproducao.voltarSegundo", StepForwardSecond = "reproducao.avancarSegundo",
        GoToStart = "reproducao.inicio", GoToEnd = "reproducao.fim", PreviousEdit = "reproducao.cortaAnterior", NextEdit = "reproducao.proximoCorte",
        Split = "linha.dividir", Delete = "linha.excluir", RippleDelete = "linha.excluirFechando", Copy = "linha.copiar", Cut = "linha.recortar",
        Paste = "linha.colar", Duplicate = "linha.duplicar", SelectAll = "linha.selecionarTudo", Group = "linha.agrupar", Ungroup = "linha.desagrupar",
        Marker = "linha.marcador", Snap = "linha.ima", ZoomIn = "linha.aproximar", ZoomOut = "linha.afastar", ZoomFit = "linha.ajustar";

    public static IReadOnlyList<ShortcutAction> All { get; } =
    [
        new(NewProject, "Novo projeto", "Arquivo e edição", "Ctrl+N"),
        new(OpenProject, "Abrir projeto", "Arquivo e edição", "Ctrl+O"),
        new(Save, "Salvar", "Arquivo e edição", "Ctrl+S"),
        new(SaveAs, "Salvar como", "Arquivo e edição", "Ctrl+Shift+S"),
        new(Import, "Importar mídia", "Arquivo e edição", "Ctrl+I"),
        new(Export, "Exportar", "Arquivo e edição", "Ctrl+E"),
        new(Undo, "Desfazer", "Arquivo e edição", "Ctrl+Z"),
        new(Redo, "Refazer", "Arquivo e edição", "Ctrl+Y", "Ctrl+Shift+Z"),
        new(Settings, "Configurações", "Arquivo e edição", "Ctrl+OemComma"),

        new(PlayPause, "Reproduzir / pausar", "Reprodução", "Space"),
        new(FullScreen, "Tela cheia", "Reprodução", "F11"),
        new(StepBack, "Quadro anterior", "Reprodução", "Left"),
        new(StepForward, "Próximo quadro", "Reprodução", "Right"),
        new(StepBackSecond, "Voltar 1 segundo", "Reprodução", "Shift+Left"),
        new(StepForwardSecond, "Avançar 1 segundo", "Reprodução", "Shift+Right"),
        new(GoToStart, "Ir para o início", "Reprodução", "Home"),
        new(GoToEnd, "Ir para o fim", "Reprodução", "End"),
        new(PreviousEdit, "Corte anterior", "Reprodução", "Up"),
        new(NextEdit, "Próximo corte", "Reprodução", "Down"),

        new(Split, "Dividir clipe no cursor", "Linha do tempo", "S", "Ctrl+K"),
        new(Delete, "Excluir", "Linha do tempo", "Delete", "Back"),
        new(RippleDelete, "Excluir e fechar o espaço", "Linha do tempo", "Shift+Delete"),
        new(Copy, "Copiar", "Linha do tempo", "Ctrl+C"),
        new(Cut, "Recortar", "Linha do tempo", "Ctrl+X"),
        new(Paste, "Colar", "Linha do tempo", "Ctrl+V"),
        new(Duplicate, "Duplicar", "Linha do tempo", "Ctrl+D"),
        new(SelectAll, "Selecionar tudo", "Linha do tempo", "Ctrl+A"),
        new(Group, "Agrupar", "Linha do tempo", "Ctrl+G"),
        new(Ungroup, "Desagrupar", "Linha do tempo", "Ctrl+Shift+G"),
        new(Marker, "Adicionar marcador", "Linha do tempo", "M"),
        new(Snap, "Ligar/desligar ímã", "Linha do tempo", "N"),
        new(ZoomIn, "Aproximar", "Linha do tempo", "Ctrl+OemPlus", "Ctrl+Add"),
        new(ZoomOut, "Afastar", "Linha do tempo", "Ctrl+OemMinus", "Ctrl+Subtract"),
        new(ZoomFit, "Ajustar à janela", "Linha do tempo", "Shift+Z"),
    ];

    public static ShortcutAction? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}

/// <summary>
/// Atalhos em vigor (RF212): os padrões do <see cref="ShortcutCatalog"/>, substituídos pelos que o usuário alterou em
/// <see cref="AppSettings.Shortcuts"/> (texto vazio = sem atalho). Uma ação alterada usa só a combinação escolhida.
/// </summary>
public sealed class ShortcutService
{
    private readonly ISettingsService? _settings;
    private Dictionary<string, IReadOnlyList<KeyChord>> _map = [];

    public ShortcutService(ISettingsService? settings = null)
    {
        _settings = settings;
        Rebuild(settings?.Current.Shortcuts ?? new Dictionary<string, string>());
        if (settings is not null) settings.Changed += (_, s) => Rebuild(s.Shortcuts);
    }

    /// <summary>Os atalhos mudaram (as telas recriam as associações de teclas).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<KeyChord> ChordsFor(string actionId) => _map.TryGetValue(actionId, out var c) ? c : [];

    /// <summary>Ação associada à tecla pressionada, ou null.</summary>
    public string? Find(Key key, ModifierKeys modifiers)
    {
        var chord = new KeyChord(key, modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt));
        foreach (var (id, chords) in _map)
            if (chords.Contains(chord)) return id;
        return null;
    }

    public bool Matches(string actionId, Key key, ModifierKeys modifiers) => Find(key, modifiers) == actionId;

    /// <summary>Texto do atalho principal para dicas e menus (vazio quando não há).</summary>
    public string Display(string actionId) => ChordsFor(actionId) is { Count: > 0 } c ? c[0].Display : "";

    /// <summary>Atalhos em vigor para um conjunto de alterações (usado pela tela de configurações antes de salvar).</summary>
    public static Dictionary<string, IReadOnlyList<KeyChord>> Resolve(IReadOnlyDictionary<string, string> overrides)
    {
        var map = new Dictionary<string, IReadOnlyList<KeyChord>>();
        foreach (var action in ShortcutCatalog.All)
        {
            var texts = overrides.TryGetValue(action.Id, out var custom) ? [custom] : action.Defaults;
            map[action.Id] = texts.Select(t => KeyChord.TryParse(t, out var c) ? c : (KeyChord?)null)
                .Where(c => c is not null).Select(c => c!.Value).ToArray();
        }
        return map;
    }

    /// <summary>Combinações usadas por mais de uma ação: combinação → ações.</summary>
    public static IReadOnlyDictionary<KeyChord, IReadOnlyList<string>> Conflicts(IReadOnlyDictionary<string, IReadOnlyList<KeyChord>> map) =>
        map.SelectMany(kv => kv.Value.Distinct().Select(c => (Chord: c, Id: kv.Key)))
            .GroupBy(x => x.Chord)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Id).ToList());

    private void Rebuild(IReadOnlyDictionary<string, string> overrides)
    {
        _map = Resolve(overrides);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
