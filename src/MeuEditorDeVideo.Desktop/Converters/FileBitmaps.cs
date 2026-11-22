using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace MeuEditorDeVideo.Desktop.Converters;

/// <summary>
/// Caminho de um arquivo de imagem (figurinhas dos pacotes de conteúdo) → bitmap; null quando o arquivo não existe ou
/// não é uma imagem que o Avalonia leia (ex.: SVG), caso em que a tela mostra o nome.
/// </summary>
public sealed class FileBitmaps : IValueConverter
{
    public static FileBitmaps Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
