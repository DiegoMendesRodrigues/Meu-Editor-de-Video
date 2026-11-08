namespace MeuEditorDeVideo.MediaEngine;

/// <summary>
/// Executa trabalho do FFmpeg fora da thread da interface, limitando a concorrência
/// para não saturar CPU e disco durante importações grandes.
/// </summary>
internal static class MediaWorkers
{
    private static readonly SemaphoreSlim Slots = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 8));

    public static async Task<T> RunAsync<T>(Func<T> work, CancellationToken ct)
    {
        await Slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(work, ct).ConfigureAwait(false);
        }
        finally
        {
            Slots.Release();
        }
    }
}
