// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Globalization;
using Avalonia.Data.Converters;

namespace MeuEditorDeVideo.Desktop.Converters;

/// <summary>Mostra um texto de origem (pt-BR) no idioma da interface, sem mudar o valor ligado.</summary>
public sealed class TranslateConverter : IValueConverter
{
    public static TranslateConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? Translator.T(s) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}

/// <summary>Inverte um valor booleano (ex.: "Sequência inteira" = !ExportRange).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public static InverseBooleanConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
