using System.Globalization;
using System.Windows.Data;
using MeuEditorDeVideo.App.Localization;

namespace MeuEditorDeVideo.App.Converters;

/// <summary>Mostra um texto de origem (pt-BR) no idioma da interface, sem mudar o valor ligado.</summary>
public sealed class TranslateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? Translator.T(s) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value;
}
