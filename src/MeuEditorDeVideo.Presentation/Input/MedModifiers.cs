namespace MeuEditorDeVideo.Presentation.Input;

/// <summary>Modificadores de teclado; mesmos valores do <c>ModifierKeys</c> do WPF. No macOS, <see cref="Control"/> é o Cmd.</summary>
[Flags]
public enum MedModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}
