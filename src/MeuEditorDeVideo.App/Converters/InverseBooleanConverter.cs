using System.Globalization;
using System.Windows.Data;

namespace MeuEditorDeVideo.App.Converters;

/// <summary>Inverte um valor booleano (ex.: "Sequência inteira" = !ExportRange).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
