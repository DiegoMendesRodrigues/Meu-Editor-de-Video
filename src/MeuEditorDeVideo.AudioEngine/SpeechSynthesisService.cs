using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.AudioEngine;

/// <summary>
/// Texto para fala com as vozes instaladas no Windows (SAPI 5, COM), 100% local. Gera um WAV 48 kHz 16 bits mono.
/// O COM do SAPI é usado por ligação tardia (sem pacote extra) numa thread STA dedicada.
/// No Linux e no macOS ainda não há implementação: <see cref="IsAvailable"/> é falso.
/// </summary>
public sealed class SpeechSynthesisService : ISpeechSynthesisService
{
    // SpeechAudioFormatType.SAFT48kHz16BitMono e SpeechStreamFileMode.SSFMCreateForWrite.
    private const int Format48kMono = 38;
    private const int CreateForWrite = 3;

    public bool IsAvailable => OperatingSystem.IsWindows() && Type.GetTypeFromProgID("SAPI.SpVoice") is not null;

    public IReadOnlyList<string> Voices() => OperatingSystem.IsWindows() && IsAvailable ? WindowsVoices() : [];

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> WindowsVoices()
    {
        return RunSta(() =>
        {
            dynamic voice = Create("SAPI.SpVoice");
            try
            {
                var names = new List<string>();
                dynamic tokens = voice.GetVoices();
                for (var i = 0; i < (int)tokens.Count; i++) names.Add((string)tokens.Item(i).GetDescription());
                return (IReadOnlyList<string>)names;
            }
            finally { Marshal.FinalReleaseComObject(voice); }
        });
    }

    public Task SynthesizeAsync(string text, string? voiceName, int rate, string outputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Digite o texto a ser falado.", nameof(text));
        if (!OperatingSystem.IsWindows() || !IsAvailable) throw new InvalidOperationException("O Windows não tem vozes de texto para fala instaladas.");
        return WindowsSynthesizeAsync(text, voiceName, rate, outputPath, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static Task WindowsSynthesizeAsync(string text, string? voiceName, int rate, string outputPath, CancellationToken cancellationToken)
    {
        return Task.Run(() => RunSta(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            var temp = outputPath + ".tmp.wav";
            dynamic voice = Create("SAPI.SpVoice");
            dynamic stream = Create("SAPI.SpFileStream");
            dynamic format = Create("SAPI.SpAudioFormat");
            try
            {
                format.Type = Format48kMono;
                stream.Format = format;
                stream.Open(temp, CreateForWrite, false);
                if (!string.IsNullOrEmpty(voiceName))
                {
                    dynamic tokens = voice.GetVoices();
                    for (var i = 0; i < (int)tokens.Count; i++)
                    {
                        dynamic token = tokens.Item(i);
                        if ((string)token.GetDescription() == voiceName)
                        {
                            voice.Voice = token;
                            break;
                        }
                    }
                }
                voice.Rate = Math.Clamp(rate, -10, 10);
                voice.AudioOutputStream = stream;
                voice.Speak(text, 0);
                stream.Close();
            }
            finally
            {
                Marshal.FinalReleaseComObject(format);
                Marshal.FinalReleaseComObject(stream);
                Marshal.FinalReleaseComObject(voice);
            }
            File.Move(temp, outputPath, overwrite: true);
            return true;
        }), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static object Create(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId) ?? throw new InvalidOperationException("SAPI indisponível."))!;

    [SupportedOSPlatform("windows")]
    private static T RunSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true, Name = "Texto para fala" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("Falha no texto para fala: " + error.Message, error);
        return result;
    }
}
