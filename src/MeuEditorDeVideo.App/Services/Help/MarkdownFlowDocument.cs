using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MeuEditorDeVideo.App.Services.Help;

/// <summary>Título do manual com o bloco correspondente no documento (para o sumário).</summary>
public sealed record HelpTopic(string Title, int Level, Block Target);

/// <summary>Monta o <see cref="FlowDocument"/> da ajuda a partir dos blocos do manual, com as cores do tema atual.</summary>
public static class MarkdownFlowDocument
{
    public static FlowDocument Build(IReadOnlyList<MdBlock> blocks, out IReadOnlyList<HelpTopic> topics)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(24, 16, 24, 32),
            TextAlignment = TextAlignment.Left,
        };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "Med.Text");
        doc.SetResourceReference(FlowDocument.BackgroundProperty, "Med.Background");

        var list = new List<HelpTopic>();
        foreach (var block in blocks)
        {
            Block element = block switch
            {
                MdHeading h => Heading(h),
                MdParagraph p => Paragraph(p.Inlines),
                MdNote n => Note(n),
                MdList l => List(l),
                MdTable t => Table(t),
                _ => new Paragraph(),
            };
            if (block is MdHeading heading && heading.Level <= 3) list.Add(new HelpTopic(heading.Text, heading.Level, element));
            doc.Blocks.Add(element);
        }

        topics = list;
        return doc;
    }

    private static Paragraph Heading(MdHeading h)
    {
        var p = Paragraph([new MdInline(h.Text)]);
        p.FontWeight = FontWeights.SemiBold;
        p.FontSize = h.Level switch { 1 => 26, 2 => 20, 3 => 16, _ => 14 };
        p.Margin = new Thickness(0, h.Level == 1 ? 0 : 18, 0, 6);
        if (h.Level <= 2) p.SetResourceReference(TextElement.ForegroundProperty, "Med.Accent");
        return p;
    }

    private static Paragraph Paragraph(IReadOnlyList<MdInline> inlines)
    {
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8), LineHeight = 21 };
        foreach (var inline in inlines) p.Inlines.Add(Inline(inline));
        return p;
    }

    private static Inline Inline(MdInline inline)
    {
        var run = new Run(inline.Text);
        if (inline.Bold) run.FontWeight = FontWeights.SemiBold;
        if (inline.Code)
        {
            run.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            run.SetResourceReference(TextElement.BackgroundProperty, "Med.PanelAlt");
        }
        return run;
    }

    private static Block Note(MdNote note)
    {
        var p = Paragraph(note.Inlines);
        var section = new Section(p)
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 6, 6, 2),
            Margin = new Thickness(0, 4, 0, 10),
        };
        section.SetResourceReference(Block.BorderBrushProperty, "Med.Accent");
        section.SetResourceReference(TextElement.BackgroundProperty, "Med.PanelAlt");
        return section;
    }

    private static Block List(MdList list)
    {
        var element = new List
        {
            MarkerStyle = list.Numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(22, 0, 0, 0),
        };
        foreach (var item in list.Items)
        {
            var p = Paragraph(item);
            p.Margin = new Thickness(0, 0, 0, 3);
            element.ListItems.Add(new ListItem(p));
        }
        return element;
    }

    private static Block Table(MdTable table)
    {
        var columns = Math.Max(table.Header.Count, table.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
        var element = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 12), BorderThickness = new Thickness(1) };
        element.SetResourceReference(System.Windows.Documents.Table.BorderBrushProperty, "Med.Border");
        for (var c = 0; c < columns; c++) element.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        group.Rows.Add(Row(table.Header, columns, header: true));
        foreach (var row in table.Rows) group.Rows.Add(Row(row, columns, header: false));
        element.RowGroups.Add(group);
        return element;
    }

    private static TableRow Row(IReadOnlyList<IReadOnlyList<MdInline>> cells, int columns, bool header)
    {
        var row = new TableRow();
        if (header) row.SetResourceReference(TextElement.BackgroundProperty, "Med.PanelAlt");
        for (var c = 0; c < columns; c++)
        {
            var p = Paragraph(c < cells.Count ? cells[c] : []);
            p.Margin = new Thickness(0);
            if (header) p.FontWeight = FontWeights.SemiBold;
            var cell = new TableCell(p) { Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(0, 0, 1, 1) };
            cell.SetResourceReference(TableCell.BorderBrushProperty, "Med.Border");
            row.Cells.Add(cell);
        }
        return row;
    }
}
