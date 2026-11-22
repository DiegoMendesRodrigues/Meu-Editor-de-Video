using Avalonia.Controls;

namespace MeuEditorDeVideo.Desktop.Views.Panels;

public partial class ClipTextPanel : UserControl
{
    public ClipTextPanel()
    {
        InitializeComponent();
        // Os botões mostram a inicial da palavra no idioma em uso (N/I/S, B/I/U, G/I/S…).
        BotaoNegrito.Content = Inicial("Negrito");
        BotaoItalico.Content = Inicial("Itálico");
        LetraSublinhado.Text = Inicial("Sublinhado");
    }

    private static string Inicial(string nome) => Translator.T(nome)[..1];
}
