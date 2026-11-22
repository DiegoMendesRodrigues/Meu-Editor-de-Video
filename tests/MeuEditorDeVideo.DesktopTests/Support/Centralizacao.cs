using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace MeuEditorDeVideo.DesktopTests.Support;

/// <summary>Mede se o conteúdo desenhado de cada botão visível fica no centro dele.</summary>
public static class Centralizacao
{
    private const double Folga = 3;

    /// <summary>Botões fora do centro (vazio = todos centralizados).</summary>
    public static IReadOnlyList<string> Desvios(Visual raiz)
    {
        var erros = new List<string>();
        foreach (var botao in raiz.GetVisualDescendants().OfType<Button>())
        {
            // Só os botões do aplicativo: fora os de modelos de controles (rolagem, seletores) e caixas de seleção.
            if (botao.TemplatedParent is not null || botao is CheckBox or RadioButton) continue;
            if (!botao.IsEffectivelyVisible || botao.Bounds.Width <= 0 || botao.Bounds.Height <= 0) continue;

            var partes = new List<(Control Controle, Rect Area)>();
            foreach (var c in botao.GetVisualDescendants().OfType<Control>())
            {
                if (c is not (TextBlock or Image or Shape) || !c.IsEffectivelyVisible) continue;
                if (c is TextBlock { Inlines: null or { Count: 0 } } vazio && string.IsNullOrEmpty(vazio.Text)) continue;
                if (c.TranslatePoint(default, botao) is not { } o) continue;
                var area = new Rect(o, c.Bounds.Size);
                if (c is TextBlock t)
                {
                    var largura = Math.Min(t.TextLayout.Width, area.Width);
                    var x = t.TextAlignment switch
                    {
                        TextAlignment.Center => (area.Width - largura) / 2,
                        TextAlignment.Right => area.Width - largura,
                        _ => 0,
                    };
                    area = new Rect(area.X + x, area.Y, largura, Math.Min(t.TextLayout.Height, area.Height));
                    if (t.TextLayout.TextLines.Count > 1 && t.TextAlignment != TextAlignment.Center)
                        erros.Add($"{Nome(botao)}: texto de várias linhas sem TextAlignment=Center");
                }
                partes.Add((c, area));
            }
            if (partes.Count == 0) continue;

            var uniao = partes.Select(p => p.Area).Aggregate((a, b) => a.Union(b));
            var cartao = botao.Classes.Contains("ins-card");
            // No cartão a miniatura ocupa a largura toda: quem tem de estar no centro é cada texto.
            var horizontais = cartao ? partes.Where(p => p.Controle is TextBlock).Select(p => p.Area) : [uniao];
            foreach (var r in horizontais)
                if (Math.Abs(r.Center.X - botao.Bounds.Width / 2) > Folga)
                    erros.Add($"{Nome(botao)}: horizontal {r.Center.X - botao.Bounds.Width / 2:+0.0;-0.0}");
            if (cartao) continue;
            // Conteúdo numa linha só: cada parte no meio da altura; empilhado: o conjunto.
            var linha = partes.Max(p => p.Area.Top) < partes.Min(p => p.Area.Bottom);
            foreach (var r in linha ? partes.Select(p => p.Area) : [uniao])
                if (Math.Abs(r.Center.Y - botao.Bounds.Height / 2) > Folga)
                    erros.Add($"{Nome(botao)}: vertical {r.Center.Y - botao.Bounds.Height / 2:+0.0;-0.0}");
        }
        return erros;
    }

    private static string Nome(Button b) =>
        $"{b.GetType().Name}[{string.Join('.', b.Classes.Where(c => !c.StartsWith(':')))}] "
        + $"\"{Avalonia.Automation.AutomationProperties.GetName(b) ?? b.Content as string ?? b.Name}\" em "
        + (b.GetVisualAncestors().FirstOrDefault(a => a is UserControl or Window)?.GetType().Name ?? "?");
}
