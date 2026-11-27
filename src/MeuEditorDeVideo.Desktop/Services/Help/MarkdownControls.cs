// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using MeuEditorDeVideo.Presentation.Services.Help;

namespace MeuEditorDeVideo.Desktop.Services.Help;

/// <summary>Título do manual com o controle correspondente no conteúdo (para o sumário).</summary>
public sealed record HelpTopic(string Title, int Level, Control Target);

/// <summary>Resultado de <see cref="MarkdownControls.Build"/>: o painel, os títulos e os blocos de texto (para a busca).</summary>
public sealed record MarkdownContent(Panel Root, IReadOnlyList<HelpTopic> Topics, IReadOnlyList<SelectableTextBlock> Texts);

/// <summary>
/// Transforma os blocos do manual (<see cref="MarkdownDocument"/>) em controles do Avalonia, com as cores do tema
/// atual (recursos <c>Med.*</c>, que acompanham a troca de tema). Substitui o <c>FlowDocument</c> do WPF: cada
/// parágrafo é um <see cref="SelectableTextBlock"/> (seleção e Ctrl+C dentro do bloco).
/// </summary>
public static class MarkdownControls
{
    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <param name="baseFolder">Pasta do manual, de onde saem as imagens (sem ela, as imagens são ignoradas).</param>
    public static MarkdownContent Build(IReadOnlyList<MdBlock> blocks, string? baseFolder = null)
    {
        var root = new StackPanel { Margin = new Thickness(24, 16, 24, 32), Spacing = 0 };
        var topics = new List<HelpTopic>();
        var texts = new List<SelectableTextBlock>();

        foreach (var block in blocks)
        {
            Control element = block switch
            {
                MdHeading h => Heading(h, texts),
                MdParagraph p => Paragraph(p.Inlines, texts, new Thickness(0, 0, 0, 8)),
                MdNote n => Note(n, texts),
                MdList l => List(l, texts),
                MdTable t => Table(t, texts),
                MdImage i => Image(i, baseFolder, texts),
                _ => new Border(),
            };
            if (block is MdHeading { Level: <= 3 } heading) topics.Add(new HelpTopic(heading.Text, heading.Level, element));
            root.Children.Add(element);
        }
        return new MarkdownContent(root, topics, texts);
    }

    /// <summary>Liga uma propriedade a um recurso do tema, atualizada quando o tema muda.</summary>
    private static void Themed(AvaloniaObject target, AvaloniaProperty property, Control host, string key) =>
        target.Bind(property, host.GetResourceObservable(key));

    private static SelectableTextBlock Paragraph(IReadOnlyList<MdInline> inlines, List<SelectableTextBlock> texts, Thickness margin)
    {
        var block = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 21,
            Margin = margin,
        };
        foreach (var inline in inlines)
        {
            var run = new Run(inline.Text);
            if (inline.Bold) run.FontWeight = FontWeight.SemiBold;
            if (inline.Code)
            {
                run.FontFamily = CodeFont;
                Themed(run, TextElement.BackgroundProperty, block, "Med.PanelAlt");
            }
            block.Inlines!.Add(run);
        }
        texts.Add(block);
        return block;
    }

    private static Control Heading(MdHeading h, List<SelectableTextBlock> texts)
    {
        var block = Paragraph([new MdInline(h.Text)], texts, new Thickness(0, h.Level == 1 ? 0 : 18, 0, 6));
        block.FontWeight = FontWeight.SemiBold;
        block.FontSize = h.Level switch { 1 => 26, 2 => 20, 3 => 16, _ => 14 };
        block.LineHeight = double.NaN;
        if (h.Level <= 2) Themed(block, TextBlock.ForegroundProperty, block, "Med.Accent");
        AutomationProperties.SetName(block, h.Text);
        return block;
    }

    private static Control Note(MdNote note, List<SelectableTextBlock> texts)
    {
        var text = Paragraph(note.Inlines, texts, new Thickness(0));
        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 6, 6, 6),
            Margin = new Thickness(0, 4, 0, 10),
            Child = text,
        };
        Themed(border, Border.BorderBrushProperty, border, "Med.Accent");
        Themed(border, Border.BackgroundProperty, border, "Med.PanelAlt");
        return border;
    }

    private static Control List(MdList list, List<SelectableTextBlock> texts)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Spacing = 3 };
        var number = 1;
        foreach (var item in list.Items)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*") };
            var marker = new TextBlock
            {
                Text = list.Numbered ? $"{number++}." : "•",
                FontSize = 14,
                LineHeight = 21,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 8, 0),
            };
            var body = Paragraph(item, texts, new Thickness(0));
            Grid.SetColumn(body, 1);
            row.Children.Add(marker);
            row.Children.Add(body);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static Control Table(MdTable table, List<SelectableTextBlock> texts)
    {
        var columns = Math.Max(table.Header.Count, table.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
        var grid = new Grid();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        var rows = new List<(IReadOnlyList<IReadOnlyList<MdInline>> Cells, bool Header)> { (table.Header, true) };
        rows.AddRange(table.Rows.Select(r => (r, false)));
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < columns; c++)
            {
                var text = Paragraph(c < rows[r].Cells.Count ? rows[r].Cells[c] : [], texts, new Thickness(0));
                if (rows[r].Header) text.FontWeight = FontWeight.SemiBold;
                var cell = new Border { BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(8, 4), Child = text };
                Themed(cell, Border.BorderBrushProperty, cell, "Med.Border");
                if (rows[r].Header) Themed(cell, Border.BackgroundProperty, cell, "Med.PanelAlt");
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        var frame = new Border { BorderThickness = new Thickness(1, 1, 0, 0), Margin = new Thickness(0, 4, 0, 12), Child = grid };
        Themed(frame, Border.BorderBrushProperty, frame, "Med.Border");
        return frame;
    }

    /// <summary>Captura de tela com a descrição embaixo; se o arquivo faltar, fica só a descrição.</summary>
    private static Control Image(MdImage image, string? baseFolder, List<SelectableTextBlock> texts)
    {
        var panel = new StackPanel();
        var path = baseFolder is null ? null : Path.GetFullPath(Path.Combine(baseFolder, image.Path));
        if (path is not null && path.StartsWith(Path.GetFullPath(baseFolder!), StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            try
            {
                var bitmap = new Bitmap(path);
                var element = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    MaxWidth = Math.Min(bitmap.PixelSize.Width, 900),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                AutomationProperties.SetName(element, image.Alt);
                panel.Children.Add(element);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
            {
                // Imagem ilegível: mostra só a descrição.
            }
        }
        var caption = Paragraph([new MdInline(image.Alt)], texts, new Thickness(0, 2, 0, 12));
        caption.FontSize = 12;
        caption.FontStyle = FontStyle.Italic;
        Themed(caption, TextBlock.ForegroundProperty, caption, "Med.TextMuted");
        panel.Children.Add(caption);
        return panel;
    }
}
