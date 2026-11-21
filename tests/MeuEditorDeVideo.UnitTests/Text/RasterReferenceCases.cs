using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.UnitTests.Text;

/// <summary>
/// Casos de referência de textos e formas. As imagens em <c>Text/Referencias</c> foram geradas pela implementação
/// anterior (pilha de desenho do WPF) e servem para garantir que projetos existentes mantenham a aparência.
/// </summary>
public static class RasterReferenceCases
{
    public sealed record TextCase(string Name, string Text, int Visible, TextStyle Style, double Scale, int FrameWidth);

    public sealed record ShapeCase(string Name, ShapeStyle Style, double Scale);

    public static readonly TextCase[] Texts =
    [
        new("simples", "Olá, mundo!", int.MaxValue, new TextStyle { FontSize = 60 }, 1, 1280),
        new("negrito_italico_sublinhado", "Ação & Ênfase", int.MaxValue,
            new TextStyle { FontSize = 56, Bold = true, Italic = true, Underline = true, Color = "#FFFFD700" }, 1, 1280),
        new("quebra_esquerda", "Um texto bem comprido que precisa quebrar em várias linhas dentro do limite", int.MaxValue,
            new TextStyle { FontSize = 40, Align = TextAlign.Left, MaxWidth = 0.5 }, 1, 1280),
        new("linhas_direita_espacamento", "Primeira linha\nSegunda\nTerceira, mais longa", int.MaxValue,
            new TextStyle { FontSize = 36, Align = TextAlign.Right, LineSpacing = 1.5 }, 1, 1280),
        new("contorno_sombra", "Contorno", int.MaxValue,
            new TextStyle { FontSize = 80, OutlineWidth = 4, Shadow = true, ShadowBlur = 8, ShadowOffsetX = 6, ShadowOffsetY = 6 }, 1, 1280),
        new("caixa_tarja", "Maria Silva", int.MaxValue,
            new TextStyle { FontSize = 44, BackgroundColor = "#E0202020", AccentColor = "#FFFF5722", BackgroundPadding = 14, BackgroundRadius = 8, Align = TextAlign.Left }, 1, 1280),
        new("caixa_sombra", "Caixa", int.MaxValue,
            new TextStyle { FontSize = 50, BackgroundColor = "#FF1565C0", Shadow = true }, 1, 1280),
        new("maquina_escrever", "Digitando aos poucos", 9, new TextStyle { FontSize = 48 }, 1, 1280),
        new("arial_escala", "Arial 2x", int.MaxValue, new TextStyle { FontFamily = "Arial", FontSize = 30 }, 2, 1920),
    ];

    public static readonly ShapeCase[] Shapes =
    [
        new("retangulo_arredondado", new ShapeStyle { Kind = ShapeKind.Rectangle, Width = 200, Height = 120, CornerRadius = 20, StrokeWidth = 6 }, 1),
        new("elipse", new ShapeStyle { Kind = ShapeKind.Ellipse, Width = 180, Height = 120, FillColor = "#FF43A047" }, 1),
        new("linha", new ShapeStyle { Kind = ShapeKind.Line, Width = 240, Height = 12, FillColor = "#FFFFFFFF" }, 1),
        new("seta", new ShapeStyle { Kind = ShapeKind.Arrow, Width = 220, Height = 100, StrokeWidth = 4 }, 1),
        new("estrela", new ShapeStyle { Kind = ShapeKind.Star, Width = 160, Height = 160, Sides = 5, FillColor = "#FFFFC107" }, 1),
        new("poligono", new ShapeStyle { Kind = ShapeKind.Polygon, Width = 160, Height = 160, Sides = 6, StrokeWidth = 5, StrokeColor = "#FF000000" }, 1),
        new("balao", new ShapeStyle { Kind = ShapeKind.SpeechBubble, Width = 240, Height = 160, FillColor = "#FFFFFFFF", StrokeWidth = 4, StrokeColor = "#FF000000" }, 1),
        new("coracao", new ShapeStyle { Kind = ShapeKind.Heart, Width = 160, Height = 150, FillColor = "#FFE53935" }, 2),
    ];
}
