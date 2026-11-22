using Avalonia.Input;

namespace MeuEditorDeVideo.Desktop;

/// <summary>
/// Teclas do Avalonia ↔ teclas do Presentation. Os códigos de <see cref="Key"/> do Avalonia são os mesmos do WPF, e
/// os de <see cref="MedKey"/> também (<c>KeyMappingTests</c>), então a tecla é um cast. Modificadores: no macOS o
/// Cmd (Meta no Avalonia) faz o papel do Ctrl dos outros sistemas e vira <see cref="MedModifiers.Control"/>; a
/// tecla Control física do Mac vira <see cref="MedModifiers.Windows"/>, que os atalhos padrão não usam.
/// </summary>
public static class KeyMapping
{
    public static MedKey ToMed(this Key key) => (MedKey)(int)key;

    public static Key ToAvalonia(this MedKey key) => (Key)(int)key;

    public static MedModifiers ToMed(this KeyModifiers modifiers) => ToMed(modifiers, OperatingSystem.IsMacOS());

    public static KeyModifiers ToAvalonia(this MedModifiers modifiers) => ToAvalonia(modifiers, OperatingSystem.IsMacOS());

    public static MedModifiers ToMed(KeyModifiers modifiers, bool mac)
    {
        var result = MedModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= MedModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= MedModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= mac ? MedModifiers.Windows : MedModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= mac ? MedModifiers.Control : MedModifiers.Windows;
        return result;
    }

    public static KeyModifiers ToAvalonia(MedModifiers modifiers, bool mac)
    {
        var result = KeyModifiers.None;
        if (modifiers.HasFlag(MedModifiers.Alt)) result |= KeyModifiers.Alt;
        if (modifiers.HasFlag(MedModifiers.Shift)) result |= KeyModifiers.Shift;
        if (modifiers.HasFlag(MedModifiers.Control)) result |= mac ? KeyModifiers.Meta : KeyModifiers.Control;
        if (modifiers.HasFlag(MedModifiers.Windows)) result |= mac ? KeyModifiers.Control : KeyModifiers.Meta;
        return result;
    }
}
