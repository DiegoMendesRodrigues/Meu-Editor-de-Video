using Avalonia.Data.Converters;

namespace MeuEditorDeVideo.Desktop.Converters;

/// <summary>Conversores numéricos para <c>IsVisible</c> (no WPF eram <c>DataTrigger Value="0"</c>).</summary>
public static class NumberConverters
{
    /// <summary>Verdadeiro quando o número é diferente de zero.</summary>
    public static readonly FuncValueConverter<int, bool> IsNotZero = new(value => value != 0);
}
