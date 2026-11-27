// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using MeuEditorDeVideo.Core.Projects;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Ferramentas › Pacotes de conteúdo: lista, instala, exporta e remove pacotes locais (.medpack).</summary>
public sealed class ContentPacksWindow : Window
{
    private const string Filter = "Pacotes de conteúdo (*.medpack;*.zip)|*.medpack;*.zip";
    private readonly IContentPackStore _store;
    private readonly IDialogService _dialogs;
    private readonly ListBox _list;
    private readonly Button _export;
    private readonly Button _remove;

    public ContentPacksWindow(IContentPackStore store, IDialogService dialogs)
    {
        _store = store;
        _dialogs = dialogs;
        Title = "Pacotes de conteúdo";
        Width = 560;
        Height = 420;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable("Med.WindowBackground").ToBinding();

        var intro = DialogParts.Label(this,
            "Pacotes trazem figurinhas (Elementos gráficos), títulos (Textos) e modelos de projeto (Arquivo › Novo a partir de modelo). " +
            "Um pacote é um arquivo .medpack (ZIP) com as pastas figurinhas, titulos e modelos. Tudo fica neste computador.", 0, 10);
        _list = new ListBox { ItemTemplate = new FuncDataTemplate<Row>((row, _) => new TextBlock { Text = row?.Text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }) };
        AutomationProperties.SetName(_list, "Pacotes instalados");
        _list.SelectionChanged += (_, _) => UpdateButtons();

        var install = Button("Instalar pacote…", (_, _) => Install(), accent: true);
        _export = Button("Exportar…", (_, _) => Export());
        _remove = Button("Remover", (_, _) => Remove());
        var close = Button("Fechar", (_, _) => Close());
        close.IsCancel = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var b in new[] { install, _export, _remove, close }) buttons.Children.Add(b);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(intro);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        Content = root;
        Refresh();
    }

    /// <summary>Algum pacote foi instalado ou removido (a biblioteca precisa recarregar).</summary>
    public bool Changed { get; private set; }

    private sealed record Row(ContentPackInfo Pack)
    {
        public string Text => Pack.Name + (Pack.IsBuiltIn ? Translator.T(" (incluído no programa)") : "") + "  —  "
            + Translator.F("{0} figurinha(s), {1} título(s), {2} modelo(s)", Pack.Stickers, Pack.Titles, Pack.Templates);

        public override string ToString() => Text;
    }

    private void Refresh()
    {
        _list.ItemsSource = _store.Packs().Select(p => new Row(p)).ToList();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _export.IsEnabled = _list.SelectedItem is Row;
        _remove.IsEnabled = _list.SelectedItem is Row { Pack.IsBuiltIn: false };
    }

    private void Install()
    {
        var path = _dialogs.PickFile("Instalar pacote de conteúdo", Filter, null);
        if (path is null) return;
        try
        {
            var info = _store.Install(path);
            Changed = true;
            Refresh();
            _dialogs.ShowInfo("Pacote instalado", $"\"{info.Name}\": {info.Stickers} figurinha(s), {info.Titles} título(s) e {info.Templates} modelo(s).");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowInfo("Instalar pacote", ex.Message);
        }
    }

    private void Export()
    {
        if (_list.SelectedItem is not Row row) return;
        var path = _dialogs.PickSaveFile("Exportar pacote", "Pacote de conteúdo (*.medpack)|*.medpack", row.Pack.Name + ".medpack", null);
        if (path is null) return;
        try { _store.Export(row.Pack.Id, path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowInfo("Exportar pacote", "Não foi possível gravar o pacote: " + ex.Message);
        }
    }

    private void Remove()
    {
        if (_list.SelectedItem is not Row row) return;
        if (!_dialogs.Confirm("Remover pacote", $"Remover o pacote \"{row.Pack.Name}\" deste computador? Projetos que já usam as figurinhas dele continuam com as cópias importadas.")) return;
        try
        {
            _store.Remove(row.Pack.Id);
            Changed = true;
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _dialogs.ShowInfo("Remover pacote", ex.Message);
        }
    }

    private static Button Button(string text, EventHandler<RoutedEventArgs> click, bool accent = false)
    {
        var b = new Button { Content = text, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
        if (accent) b.Classes.Add("med-accent");
        AutomationProperties.SetName(b, text.TrimEnd('…'));
        b.Click += click;
        return b;
    }
}
