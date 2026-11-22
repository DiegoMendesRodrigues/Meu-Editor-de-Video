using Avalonia.Threading;

namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>
/// Espera uma tarefa da interface (diálogo, seletor de arquivos) sem bloquear o desenho: roda um quadro aninhado do
/// despachante até ela terminar. Mantém síncronos os métodos de <see cref="IDialogService"/> pedidos pelos ViewModels,
/// como no WPF (ShowDialog).
/// </summary>
public static class ModalLoop
{
    public static T Run<T>(Task<T> task)
    {
        Wait(task);
        return task.GetAwaiter().GetResult();
    }

    public static void Run(Task task)
    {
        Wait(task);
        task.GetAwaiter().GetResult();
    }

    private static void Wait(Task task)
    {
        if (task.IsCompleted) return;
        Dispatcher.UIThread.VerifyAccess();
        var frame = new DispatcherFrame();
        // Postar acorda o laço do despachante; só mudar Continue de outra thread não o acordaria.
        task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false, DispatcherPriority.Send),
            TaskScheduler.Default);
        Dispatcher.UIThread.PushFrame(frame);
    }
}
