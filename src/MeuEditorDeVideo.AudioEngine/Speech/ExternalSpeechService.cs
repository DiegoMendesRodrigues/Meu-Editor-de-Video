using System.Diagnostics;
using System.Globalization;
using System.Text;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.AudioEngine.Speech;

/// <summary>
/// Base do texto para fala por programa externo (<c>say</c> no macOS, <c>espeak-ng</c> no Linux), 100% local. Os processos são
/// chamados sem shell (<see cref="ProcessStartInfo.ArgumentList"/>), o texto vai por arquivo temporário e a saída é gravada
/// em arquivo temporário e movida no fim, como no Windows. O resultado é sempre um WAV 48 kHz 16 bits mono.
/// </summary>
public abstract class ExternalSpeechService : ISpeechSynthesisService
{
    public const int OutputSampleRate = 48000;
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(10);

    private IReadOnlyList<SpeechVoice>? _voices;
    private string? _tool;
    private bool _toolSearched;

    /// <summary>Caminhos candidatos do programa, em ordem; os nomes sem diretório são procurados no PATH.</summary>
    protected abstract IReadOnlyList<string> ToolCandidates { get; }

    public abstract string UnavailableReason { get; }

    protected abstract string[] ListArguments { get; }

    protected abstract IReadOnlyList<SpeechVoice> ParseVoices(string output);

    /// <summary>Argumentos da síntese. <paramref name="voice"/> é nulo para a voz padrão do sistema.</summary>
    protected abstract IReadOnlyList<string> SynthesisArguments(string? voice, int rate, string textFile, string wavFile);

    protected string? ToolPath
    {
        get
        {
            if (!_toolSearched)
            {
                _tool = ToolCandidates.Select(Locate).FirstOrDefault(p => p is not null);
                _toolSearched = true;
            }
            return _tool;
        }
    }

    public bool IsAvailable => ToolPath is not null;

    public IReadOnlyList<string> Voices() => LoadVoices().Select(v => v.Display).ToList();

    private IReadOnlyList<SpeechVoice> LoadVoices()
    {
        if (_voices is not null) return _voices;
        var tool = ToolPath ?? throw new InvalidOperationException(UnavailableReason);
        var (exit, output, error) = Run(tool, ListArguments, ListTimeout, CancellationToken.None, inheritNothing: true);
        if (exit != 0) throw new InvalidOperationException("Não foi possível listar as vozes: " + FirstLine(error));
        return _voices = SpeechParsing.OrderForCulture(ParseVoices(output), CultureInfo.CurrentUICulture);
    }

    public async Task SynthesizeAsync(string text, string? voiceName, int rate, string outputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Digite o texto a ser falado.", nameof(text));
        var tool = ToolPath ?? throw new InvalidOperationException(UnavailableReason);
        string? voice = null;
        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            voice = SpeechParsing.ResolveVoice(LoadVoices(), voiceName)
                ?? throw new InvalidOperationException("A voz escolhida não está instalada: " + voiceName);
        }
        else
        {
            // Sem voz: a do idioma da interface (a lista já vem ordenada assim) se houver; senão, a padrão do programa.
            var first = LoadVoices().FirstOrDefault();
            if (first is not null && MatchesCulture(first)) voice = first.Name;
        }
        if (voice is not null && voice.StartsWith('-')) throw new InvalidOperationException("Nome de voz inválido: " + voice);

        cancellationToken.ThrowIfCancellationRequested();
        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        var textFile = fullOutput + ".tmp.txt";
        var rawWav = fullOutput + ".raw.wav";
        var finalTemp = fullOutput + ".tmp.wav";
        try
        {
            await File.WriteAllTextAsync(textFile, text, new UTF8Encoding(false), cancellationToken);
            var args = SynthesisArguments(voice, rate, textFile, rawWav);
            var (exit, _, error) = await Task.Run(() => Run(tool, args, Timeout.InfiniteTimeSpan, cancellationToken, inheritNothing: true), cancellationToken);
            if (exit != 0) throw new InvalidOperationException("Falha no texto para fala: " + FirstLine(error));
            if (!File.Exists(rawWav) || new FileInfo(rawWav).Length == 0) throw new InvalidOperationException("O texto para fala não gerou áudio.");

            var wav = WavAudio.Parse(await File.ReadAllBytesAsync(rawWav, cancellationToken)).ToMono(OutputSampleRate);
            await File.WriteAllBytesAsync(finalTemp, wav.ToBytes(), cancellationToken);
            File.Move(finalTemp, fullOutput, overwrite: true);
        }
        finally
        {
            TryDelete(textFile);
            TryDelete(rawWav);
            TryDelete(finalTemp);
        }
    }

    private static bool MatchesCulture(SpeechVoice voice)
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var l = voice.Language.Replace('-', '_');
        return l.Equals(lang, StringComparison.OrdinalIgnoreCase) || l.StartsWith(lang + "_", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Locate(string candidate)
    {
        if (Path.IsPathRooted(candidate)) return File.Exists(candidate) ? candidate : null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir, candidate);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>Executa o programa sem shell. Estoura o tempo ou cancela: o processo (e os filhos) é encerrado.</summary>
    private static (int ExitCode, string Output, string Error) Run(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct, bool inheritNothing)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = inheritNothing,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException("Não foi possível executar o texto para fala: " + ex.Message, ex);
        }
        if (inheritNothing) process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var registration = ct.Register(() => Kill(process));
        var finished = timeout == Timeout.InfiniteTimeSpan ? Wait(process) : process.WaitForExit(timeout);
        if (!finished)
        {
            Kill(process);
            process.WaitForExit();
            throw new InvalidOperationException("O programa de texto para fala demorou demais e foi interrompido.");
        }
        process.WaitForExit(); // garante o esvaziamento das saídas assíncronas
        ct.ThrowIfCancellationRequested();
        lock (stdout) lock (stderr) return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static bool Wait(Process p) { p.WaitForExit(); return true; }

    private static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* já encerrou */ }
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "sem detalhes";

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* arquivo temporário: ignorar */ }
        catch (UnauthorizedAccessException) { /* idem */ }
    }
}

/// <summary>macOS: <c>/usr/bin/say</c>, que já grava WAV 48 kHz 16 bits mono (a normalização do WAV é só uma garantia).</summary>
public sealed class MacSpeechSynthesisService : ExternalSpeechService
{
    protected override IReadOnlyList<string> ToolCandidates => ["/usr/bin/say"];

    public override string UnavailableReason => "O comando 'say' do macOS não foi encontrado; o texto para fala não está disponível.";

    protected override string[] ListArguments => ["-v", "?"];

    protected override IReadOnlyList<SpeechVoice> ParseVoices(string output) => SpeechParsing.ParseSayVoices(output);

    protected override IReadOnlyList<string> SynthesisArguments(string? voice, int rate, string textFile, string wavFile)
    {
        var args = new List<string>();
        if (voice is not null) { args.Add("-v"); args.Add(voice); }
        args.AddRange(["-r", SpeechParsing.RateToSayWpm(rate).ToString(CultureInfo.InvariantCulture),
            "-o", wavFile, "--file-format=WAVE", "--data-format=LEI16@48000", "--channels=1", "-f", textFile]);
        return args;
    }
}

/// <summary>Linux: <c>espeak-ng</c> (pacote Debian/Ubuntu <c>espeak-ng</c>), que gera 22,05 kHz mono; o WAV é reamostrado para 48 kHz.</summary>
public sealed class LinuxSpeechSynthesisService : ExternalSpeechService
{
    protected override IReadOnlyList<string> ToolCandidates => ["espeak-ng", "/usr/bin/espeak-ng", "/usr/local/bin/espeak-ng", "/snap/bin/espeak-ng"];

    public override string UnavailableReason => "Instale o pacote espeak-ng para usar o texto para fala.";

    protected override string[] ListArguments => ["--voices"];

    protected override IReadOnlyList<SpeechVoice> ParseVoices(string output) => SpeechParsing.ParseEspeakVoices(output);

    protected override IReadOnlyList<string> SynthesisArguments(string? voice, int rate, string textFile, string wavFile)
    {
        var args = new List<string>();
        if (voice is not null) { args.Add("-v"); args.Add(voice); }
        args.AddRange(["-s", SpeechParsing.RateToEspeakWpm(rate).ToString(CultureInfo.InvariantCulture), "-w", wavFile, "-f", textFile]);
        return args;
    }
}
