using System.Diagnostics;
using System.IO;
using System.Linq;
using MeuEditorDeVideo.AIEngine.Models;
using MeuEditorDeVideo.AIEngine.Speech;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.MediaEngine.Playback;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>
/// Testes de IA §14.4 — "Reconhecimento de fala" e "Geração de legendas" com o Whisper real: uma frase em português é
/// sintetizada pela voz do Windows (SAPI, pt-BR), transcrita no computador e convertida em legendas na linha do tempo.
/// Ignorado quando o modelo Whisper não está instalado ou não há voz pt-BR no Windows.
/// </summary>
public sealed class SpeechRecognitionTests : IDisposable
{
    private const string Frase = "Olá! Este é um teste do editor de vídeo. Hoje vamos gravar um vídeo novo para a família.";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "med-fala-" + Guid.NewGuid().ToString("N"));

    public SpeechRecognitionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [WhisperFact]
    public async Task Whisper_transcreve_fala_em_portugues_e_gera_legendas_na_linha_do_tempo()
    {
        var wav = Path.Combine(_dir, "fala.wav");
        if (!Sintetizar(Frase, wav)) return; // sem voz pt-BR neste Windows

        var probe = new MediaProbe(NullLogger<MediaProbe>.Instance);
        var r = await probe.ProbeAsync(wav);
        Assert.True(r.IsOk, r.Error);
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = r.Kind, Name = "fala", OriginalPath = wav, FileSize = new FileInfo(wav).Length,
            LastWriteUtc = File.GetLastWriteTimeUtc(wav), Duration = r.Duration, Info = r.Info,
        };
        var project = new AddMediaAssetsCommand([asset]).Apply(
            Project.CreateNew("Fala", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.UtcNow));
        var seq = project.MainSequence;
        var audioTrack = seq.Tracks.First(t => t.Kind == TrackKind.Audio).Id;
        project = new AddMediaToTimelineCommand(seq.Id, asset.Id, null, audioTrack, MediaTime.Zero, asset.Duration).Apply(project);
        var clip = project.MainSequence.Tracks.First(t => t.Id == audioTrack).Clips.Single();
        var paths = new Dictionary<Guid, string> { [asset.Id] = wav };

        var registry = new ModelRegistry(WhisperFactAttribute.InstalledModels);
        var whisper = new WhisperTranscriber(registry, new AudioSampleSourceFactory(NullLoggerFactory.Instance));
        Assert.True(whisper.IsAvailable);

        var progress = new List<double>();
        var segments = await whisper.TranscribeAsync(project, clip, paths, "pt", new Progress<double>(progress.Add));

        Assert.NotEmpty(segments);
        var texto = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant();
        Assert.Contains("teste", texto);
        Assert.Contains("vídeo", texto);
        Assert.All(segments, s => Assert.True(s.Start >= clip.Start && s.End <= clip.End && s.End > s.Start, "trecho fora do clipe"));

        // Geração de legendas (RF158): os trechos viram clipes de legenda numa trilha nova, como em "Gerar legendas".
        var legendas = SubtitleOps.ToClips(segments.Select(s => new SubtitleEntry(s.Start, s.End, s.Text)), project.Settings);
        var comLegendas = new ImportSubtitlesCommand(seq.Id, legendas) { NewTrackName = "Legendas (IA)" }.Apply(project);
        var trilha = comLegendas.MainSequence.Tracks.Single(t => t.Name == "Legendas (IA)");
        Assert.Equal(segments.Count, trilha.Clips.Count);
        Assert.Equal(segments.Select(s => s.Text), SubtitleOps.FromTrack(trilha).Select(e => e.Text));
    }

    /// <summary>Gera um WAV com a voz pt-BR do Windows (SAPI) pelo PowerShell do sistema. Retorna false sem voz pt-BR.</summary>
    private static bool Sintetizar(string texto, string wav)
    {
        var script = $$"""
            $v = New-Object -ComObject SAPI.SpVoice
            $vozes = $v.GetVoices('Language=416')
            if ($vozes.Count -eq 0) { exit 3 }
            $v.Voice = $vozes.Item(0)
            $s = New-Object -ComObject SAPI.SpFileStream
            $s.Format.Type = 22
            $s.Open('{{wav.Replace("'", "''")}}', 3)
            $v.AudioOutputStream = $s
            [void]$v.Speak('{{texto.Replace("'", "''")}}')
            $s.Close()
            """;
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
        {
            UseShellExecute = false, CreateNoWindow = true,
        })!;
        p.WaitForExit(60_000);
        if (p.ExitCode == 3) return false;
        Assert.True(p.ExitCode == 0 && File.Exists(wav) && new FileInfo(wav).Length > 10_000, "falha ao sintetizar a fala");
        return true;
    }
}

/// <summary>Teste que precisa do Whisper instalado pelo gerenciador de modelos (pasta de modelos do usuário).</summary>
public sealed class WhisperFactAttribute : FactAttribute
{
    public static string InstalledModels =>
        Environment.GetEnvironmentVariable("MED_INSTALLED_MODELS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeuEditorDeVideo", "ai-models");

    public WhisperFactAttribute()
    {
        var found = Directory.Exists(InstalledModels)
            && Directory.EnumerateFiles(InstalledModels, "ggml-*.bin", SearchOption.AllDirectories).Any();
        if (!found) Skip = "Modelo Whisper não instalado (instale pelo gerenciador de modelos de IA).";
    }
}
