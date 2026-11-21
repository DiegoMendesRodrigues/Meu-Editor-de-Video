using System.Diagnostics;
using System.Text;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaEngine.Recording;

/// <summary>
/// Base da gravação de tela, webcam e microfone com o <c>ffmpeg</c> de linha de comando que acompanha o aplicativo
/// (processo, parada com "q", registro de erros). Cada sistema operacional monta as próprias entradas.
/// </summary>
public abstract class FfmpegRecorderBase : IScreenRecorder
{
    protected static string? Executable() => FFmpegRuntime.CliPath();

    public virtual bool IsAvailable => Executable() is not null;

    public virtual string? ScreenCaptureUnavailableReason => null;

    public abstract Task<CaptureDevices> ListDevicesAsync(CancellationToken ct = default);

    public abstract IReadOnlyList<ScreenArea> Screens();

    public abstract IReadOnlyList<CaptureWindow> Windows(ScreenArea? area);

    /// <summary>Resolve janela, tela e dispositivos e devolve os argumentos do ffmpeg.</summary>
    /// <exception cref="InvalidOperationException">Não dá para gravar o que foi pedido (mensagem para o usuário).</exception>
    protected abstract Task<IReadOnlyList<string>> PrepareAsync(ScreenRecordingOptions options, CancellationToken ct);

    /// <summary>Mensagem amigável para uma falha conhecida do ffmpeg neste sistema; nula se o log não é reconhecido.</summary>
    protected virtual string? FriendlyFailure(string log) => null;

    protected static string MissingExecutableMessage => $"O {FFmpegPlatform.CliName(FFmpegPlatform.Current)} não foi encontrado na pasta do aplicativo.";

    public async Task<IScreenRecording> StartAsync(ScreenRecordingOptions options, CancellationToken ct = default)
    {
        var exe = Executable() ?? throw new InvalidOperationException(MissingExecutableMessage);
        var args = await PrepareAsync(options, ct);
        var dir = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var process = Process.Start(NewStartInfo(exe, args, redirectInput: true))
                      ?? throw new InvalidOperationException("Não foi possível iniciar a gravação.");
        var recording = new Recording(process, options.OutputPath, Describe);
        // Erros de dispositivo aparecem logo: espera um pouco para avisar já no início.
        await Task.WhenAny(process.WaitForExitAsync(ct), Task.Delay(1500, ct));
        if (process.HasExited)
        {
            var log = recording.Log;
            await recording.DisposeAsync();
            throw new InvalidOperationException("A gravação não começou. " + Describe(log));
        }
        return recording;
    }

    private string Describe(string log) => FriendlyFailure(log) ?? Summary(log);

    protected static ProcessStartInfo NewStartInfo(string exe, IEnumerable<string> args, bool redirectInput = false)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = redirectInput,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>
    /// Roda uma ferramenta curta do sistema (pactl, xrandr, wmctrl…) com tempo limite e devolve a saída padrão e a de
    /// erro; nulo se a ferramenta não existe, não inicia ou estoura o tempo.
    /// </summary>
    protected static (string Output, string Error)? RunTool(string file, IEnumerable<string> args, int timeoutMs = 3000)
    {
        try
        {
            var psi = NewStartInfo(file, args);
            psi.Environment["LC_ALL"] = "C";
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                return null;
            }
            return (output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    protected static string Summary(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "Verifique se a câmera e o microfone não estão em uso por outro programa." : string.Join(" ", lines.TakeLast(3));
    }

    private sealed class Recording : IScreenRecording
    {
        private readonly Process _process;
        private readonly string _output;
        private readonly Func<string, string> _describe;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly StringBuilder _log = new();
        private readonly Task _drain;

        public Recording(Process process, string output, Func<string, string> describe)
        {
            _process = process;
            _output = output;
            _describe = describe;
            _drain = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                    lock (_log) _log.AppendLine(line);
            });
            _ = process.StandardOutput.ReadToEndAsync();
        }

        public string Log
        {
            get { lock (_log) return _log.ToString(); }
        }

        public TimeSpan Elapsed => _clock.Elapsed;

        public bool IsRunning => !_process.HasExited;

        public async Task<string> StopAsync()
        {
            _clock.Stop();
            if (!_process.HasExited)
            {
                try
                {
                    // "q" encerra o ffmpeg normalmente (o MP4 é finalizado).
                    await _process.StandardInput.WriteAsync('q');
                    await _process.StandardInput.FlushAsync();
                }
                catch (IOException) { }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await _process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); }
            }
            await Task.WhenAny(_drain, Task.Delay(2000));
            if (!File.Exists(_output) || new FileInfo(_output).Length == 0)
                throw new InvalidOperationException("A gravação falhou. " + _describe(Log));
            return _output;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await _process.WaitForExitAsync();
            }
            _process.Dispose();
        }
    }
}
