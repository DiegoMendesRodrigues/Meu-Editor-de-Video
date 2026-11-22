using Avalonia.Controls;
using MeuEditorDeVideo.Desktop.Services;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Mostra um diálogo modal sobre a janela dada (ou sobre a ativa, quando não há).</summary>
internal static class DialogShow
{
    public static bool? Modal(Window dialog, Window? owner) =>
        owner is { IsVisible: true } ? ModalLoop.Run(dialog.ShowDialog<bool?>(owner)) : DialogHost.ShowModal(dialog);
}
