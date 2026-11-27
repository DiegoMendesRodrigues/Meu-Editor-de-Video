// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Controls;
using MeuEditorDeVideo.Desktop.Controls;
using MeuEditorDeVideo.Desktop.Views.Dialogs;

namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>
/// Todo diálogo de <c>Views/Dialogs</c> cabe na área útil da tela, qualquer que seja o caminho por onde abre:
/// <list type="bullet">
/// <item>tamanho definido: encolhe até o mínimo dele (o miolo rola) e, abaixo disso, reduz o conteúdo;</item>
/// <item>altura do conteúdo: reduz o conteúdo quando ele não cabe;</item>
/// <item>largura e altura do conteúdo (caixa de mensagem): só limita, e o texto rola.</item>
/// </list>
/// </summary>
public static class JanelasNaTela
{
    // Moldura da janela, que o sistema só informa depois de mostrá-la: bordas e barra de título.
    private const double MolduraLargura = 16;
    private const double MolduraAltura = 40;

    private static bool _iniciado;

    /// <summary>Área útil da tela em que a janela vai abrir, em unidades da interface. Os testes trocam.</summary>
    public static Func<Window, Size?> AreaUtil { get; set; } = AreaDaTela;

    public static void Iniciar()
    {
        if (_iniciado) return;
        _iniciado = true;
        // Este evento sai no começo do Show, antes do primeiro layout e do cálculo da posição.
        Window.WindowOpenedEvent.AddClassHandler<Window>((janela, _) => AoAbrir(janela));
    }

    private static Size? AreaDaTela(Window janela)
    {
        // O dono ainda não foi atribuído à janela: vale a tela da janela ativa.
        var tela = (DialogHost.Owner is { } dona ? janela.Screens.ScreenFromWindow(dona) : null) ?? janela.Screens.Primary;
        return tela is { Scaling: > 0 } ? new Size(tela.WorkingArea.Width / tela.Scaling, tela.WorkingArea.Height / tela.Scaling) : null;
    }

    private static void AoAbrir(Window janela)
    {
        if (janela.GetType().Namespace != typeof(MessageWindow).Namespace || janela.Content is AjusteDeEscala) return;
        if (AreaUtil(janela) is not { } area || area.Width <= MolduraLargura || area.Height <= MolduraAltura) return;
        Ajustar(janela, new Size(area.Width - MolduraLargura, area.Height - MolduraAltura));
        janela.Opened += (_, _) => Reposicionar(janela);
        // A gravação e as mensagens de erro fazem alguns diálogos crescer depois de abertos.
        if (janela.SizeToContent != SizeToContent.Manual) janela.Resized += (_, _) => Reposicionar(janela);
    }

    private static void Ajustar(Window janela, Size area)
    {
        var modo = janela.SizeToContent;
        var largura = janela.Width;
        var minimo = new Size(janela.MinWidth > 0 ? janela.MinWidth : largura, janela.MinHeight > 0 ? janela.MinHeight : janela.Height);

        janela.MinWidth = Math.Min(janela.MinWidth, area.Width);
        janela.MinHeight = Math.Min(janela.MinHeight, area.Height);
        if (largura > area.Width) janela.Width = area.Width;
        if (modo == SizeToContent.Manual && janela.Height > area.Height) janela.Height = area.Height;
        if (modo.HasFlag(SizeToContent.Width)) janela.MaxWidth = Math.Min(janela.MaxWidth, area.Width);
        if (modo.HasFlag(SizeToContent.Height)) janela.MaxHeight = Math.Min(janela.MaxHeight, area.Height);

        AjusteDeEscala? ajuste = modo switch
        {
            SizeToContent.Height => new() { LarguraMinima = largura, AlturaMinima = double.NaN },
            SizeToContent.Manual when minimo.Width > area.Width || minimo.Height > area.Height
                => new() { LarguraMinima = minimo.Width, AlturaMinima = minimo.Height },
            _ => null,
        };
        if (ajuste is null || janela.Content is not Control conteudo) return;
        janela.Content = null;
        // A escala parte do canto do filho direto: sem a moldura, a margem do conteúdo não encolheria.
        ajuste.Child = new Border { Child = conteudo };
        janela.Content = ajuste;
    }

    private static void Reposicionar(Window janela)
    {
        if (!janela.IsVisible || janela.WindowState != WindowState.Normal) return;
        if (janela.Screens.ScreenFromWindow(janela) is not { } tela) return;
        var tamanho = PixelSize.FromSize(janela.FrameSize ?? janela.ClientSize, janela.DesktopScaling);
        var posicao = DentroDaTela(new PixelRect(janela.Position, tamanho), tela.WorkingArea);
        if (posicao != janela.Position) janela.Position = posicao;
    }

    /// <summary>Posição mais próxima em que a janela fica dentro da área; se não couber, encosta no alto e à esquerda.</summary>
    public static PixelPoint DentroDaTela(PixelRect janela, PixelRect area) => new(
        Math.Max(area.X, Math.Min(janela.X, area.Right - janela.Width)),
        Math.Max(area.Y, Math.Min(janela.Y, area.Bottom - janela.Height)));
}
