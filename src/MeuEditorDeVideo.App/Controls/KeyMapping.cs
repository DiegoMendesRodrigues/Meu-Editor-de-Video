using System.Windows.Input;

namespace MeuEditorDeVideo.App;

/// <summary>Teclas do WPF ↔ teclas do Presentation (mesmos valores: <c>KeyMappingTests</c>).</summary>
public static class KeyMapping
{
    public static MedKey ToMed(this Key key) => (MedKey)(int)key;

    public static MedModifiers ToMed(this ModifierKeys modifiers) => (MedModifiers)(int)modifiers;

    public static Key ToWpf(this MedKey key) => (Key)(int)key;

    public static ModifierKeys ToWpf(this MedModifiers modifiers) => (ModifierKeys)(int)modifiers;
}
