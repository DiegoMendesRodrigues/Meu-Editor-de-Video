using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Presentation.ViewModels.Media;

/// <summary>Linha "rótulo: valor" do painel de informações da mídia (RF035).</summary>
public sealed record MediaInfoLine(string Label, string Value)
{
    /// <summary>Rótulo no idioma da interface.</summary>
    public string Label { get; init; } = MeuEditorDeVideo.Presentation.Localization.Translator.T(Label);

    /// <summary>Texto lido por leitores de tela (itens de lista usam o texto do objeto).</summary>
    public override string ToString() => $"{Label}: {Value}";
}

/// <summary>Item da biblioteca de mídias (painel A).</summary>
public sealed partial class MediaItemViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public MediaItemViewModel(MediaAsset asset, bool isMissing)
    {
        Asset = asset;
        IsMissing = isMissing;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Category), nameof(Details), nameof(InfoLines), nameof(ToolTip))]
    public partial MediaAsset Asset { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfoLines), nameof(ToolTip))]
    public partial bool IsMissing { get; set; }

    /// <summary>Miniatura (vídeos, imagens, GIFs, sequências).</summary>
    [ObservableProperty]
    public partial BgraImage? Thumbnail { get; set; }

    /// <summary>Forma de onda normalizada (0–240 × 0–100) para áudios, como dados de caminho; desenhar com Stretch="Fill".</summary>
    [ObservableProperty]
    public partial string? Waveform { get; set; }

    [ObservableProperty]
    public partial bool PreviewFailed { get; set; }

    /// <summary>Situação do proxy 540p (pré-visualização leve de vídeos grandes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProxyLabel), nameof(HasProxyLabel), nameof(InfoLines), nameof(HasProxy), nameof(CanGenerateProxy))]
    public partial ProxyStatus? Proxy { get; set; }

    public bool HasProxy => Proxy?.State == ProxyState.Ready;

    public bool CanGenerateProxy => ProxyPolicy.Supports(Asset) && Proxy?.State is null or ProxyState.None or ProxyState.Failed;

    /// <summary>Selo na miniatura: "Proxy", "Proxy 45%" ou "Proxy falhou".</summary>
    public string ProxyLabel => Proxy?.State switch
    {
        ProxyState.Ready => "Proxy",
        ProxyState.Queued => "Proxy na fila",
        ProxyState.Generating => $"Proxy {Proxy.Progress * 100:0}%",
        ProxyState.Failed => "Proxy falhou",
        _ => "",
    };

    public bool HasProxyLabel => ProxyLabel.Length > 0;

    public bool PreviewRequested { get; set; }

    /// <summary>Picos de áudio completos (linha do tempo desenha o trecho usado por cada clipe).</summary>
    public WaveformData? Peaks { get; private set; }

    public Guid Id => Asset.Id;
    public string Name => Asset.Name;

    /// <summary>Nome lido pelos leitores de tela (o item da lista da biblioteca usa ToString).</summary>
    public override string ToString() => Name;
    public MediaKind Kind => Asset.Kind;
    public string? Category => Asset.Category;
    public bool IsAudio => Asset.Kind == MediaKind.Audio;

    public string KindLabel => KindName(Asset.Kind);

    /// <summary>Glifo Segoe Fluent Icons do tipo de mídia.</summary>
    public string KindGlyph => Asset.Kind switch
    {
        MediaKind.Video => "",
        MediaKind.Audio => "",
        MediaKind.Image => "",
        MediaKind.ImageSequence => "",
        MediaKind.AnimatedGif => "",
        _ => "",
    };

    /// <summary>Selo exibido sobre a miniatura (duração ou tipo).</summary>
    public string Badge => Asset.Kind is MediaKind.Image ? "Imagem" : FormatDuration(Asset.Duration);

    /// <summary>Linha secundária: "1920 × 1080 · 29,97 FPS" / "48 kHz · estéreo".</summary>
    public string Details => Asset.Kind switch
    {
        MediaKind.Audio => AudioSummary(Asset.Info) ?? KindLabel,
        _ => Asset.Width > 0 ? $"{Asset.Width} × {Asset.Height}" + (Asset.FrameRate is { } f ? $" · {f} FPS" : "") : KindLabel,
    };

    public string ToolTip => (IsMissing ? $"{Name}\nArquivo não encontrado: {Asset.OriginalPath}" : $"{Name}\n{Asset.OriginalPath}")
        + (Asset.Tags.Count > 0 ? "\nEtiquetas: " + string.Join(", ", Asset.Tags) : "");

    public IReadOnlyList<MediaInfoLine> InfoLines
    {
        get
        {
            var a = Asset;
            var i = a.Info ?? new MediaTechnicalInfo();
            var lines = new List<MediaInfoLine>
            {
                new("Nome", a.Name),
                new("Tipo", KindLabel),
                new("Situação", IsMissing ? "Arquivo não encontrado" : "Disponível"),
                new("Arquivo", a.Sequence is { } s ? System.IO.Path.Combine(s.Directory, s.Prefix + "#" + s.Suffix) : a.OriginalPath),
            };
            if (a.FileSize > 0) lines.Add(new("Tamanho", FormatSize(a.FileSize)));
            if (a.Kind != MediaKind.Image) lines.Add(new("Duração", FormatDuration(a.Duration)));
            if (a.Sequence is { } seq) lines.Add(new("Imagens", $"{seq.FrameCount} (nº {seq.FirstNumber} a {seq.FirstNumber + seq.FrameCount - 1})"));
            if (a.Width > 0) lines.Add(new("Resolução", $"{a.Width} × {a.Height} ({AspectRatio.FromSize(a.Width, a.Height)})"));
            if (a.FrameRate is { } fps) lines.Add(new("Taxa de quadros", $"{fps} FPS"));
            if ((i.ContainerLongName ?? i.Container) is { } c) lines.Add(new("Contêiner", c));
            if (i.VideoCodec is { } vc) lines.Add(new("Codec de vídeo", i.VideoCodecLongName is { } vl ? $"{vc} ({vl})" : vc));
            if (i.PixelFormat is { } pf) lines.Add(new("Formato de pixel", pf));
            if (a.HasVideo) lines.Add(new("Transparência", i.HasAlpha ? "Sim" : "Não"));
            if (i.Rotation != 0) lines.Add(new("Rotação", $"{i.Rotation}°"));
            if (i.AudioCodec is { } ac) lines.Add(new("Codec de áudio", i.AudioCodecLongName is { } al ? $"{ac} ({al})" : ac));
            if (AudioSummary(i) is { } audio) lines.Add(new("Áudio", audio));
            if (i.BitRate > 0) lines.Add(new("Taxa de bits", FormatBitRate(i.BitRate)));
            if (i.VideoStreams + i.AudioStreams + i.SubtitleStreams > 1)
                lines.Add(new("Faixas", $"{i.VideoStreams} de vídeo, {i.AudioStreams} de áudio, {i.SubtitleStreams} de legenda"));
            if (ProxyPolicy.Supports(a))
                lines.Add(new("Proxy de edição", Proxy?.State switch
                {
                    ProxyState.Ready => "Pronto (540p, usado na pré-visualização)",
                    ProxyState.Queued => "Na fila",
                    ProxyState.Generating => $"Gerando ({Proxy.Progress * 100:0}%)",
                    ProxyState.Failed => $"Falhou: {Proxy.Error}",
                    _ => ProxyPolicy.IsRecommended(a) ? "Não gerado (recomendado para esta resolução)" : "Não gerado",
                }));
            lines.Add(new("Categoria", a.Category ?? "Sem categoria"));
            return lines;
        }
    }

    public void SetPreview(BgraImage? image, WaveformData? waveform)
    {
        if (image is not null) Thumbnail = image;

        if (waveform is { Count: > 0 })
        {
            Peaks = waveform;
            // Na biblioteca só arquivos de áudio exibem a forma de onda; vídeos mostram a miniatura.
            if (Asset.Kind == MediaKind.Audio) Waveform = BuildWaveform(waveform);
        }
        PreviewFailed = image is null && waveform is null;
    }

    /// <summary>
    /// Polígono simétrico (máximos em cima, mínimos embaixo) reduzido a 240 colunas, na linguagem de caminhos
    /// ("M x,y L …") que os toolkits convertem em geometria. Um segundo traço invisível fixa a caixa 0–240 × 0–100
    /// para que Stretch="Fill" preserve a escala vertical.
    /// </summary>
    public static string BuildWaveform(WaveformData w)
    {
        const int columns = 240;
        var n = w.Count;
        var top = new (double X, double Y)[columns];
        var bottom = new (double X, double Y)[columns];
        for (var c = 0; c < columns; c++)
        {
            int from = (int)((long)c * n / columns), to = Math.Max(from + 1, (int)((long)(c + 1) * n / columns));
            float max = 0, min = 0;
            for (var k = from; k < to && k < n; k++)
            {
                if (w.Max[k] > max) max = w.Max[k];
                if (w.Min[k] < min) min = w.Min[k];
            }
            top[c] = (c, 50 - Math.Clamp(max, -1f, 1f) * 50);
            bottom[c] = (c, 50 - Math.Clamp(min, -1f, 1f) * 50);
        }

        var sb = new System.Text.StringBuilder("M0,50");
        void L((double X, double Y) p) => sb.Append(CultureInfo.InvariantCulture, $" L{p.X:0.###},{p.Y:0.###}");
        foreach (var p in top) L(p);
        L((columns, 50));
        for (var i = columns - 1; i >= 0; i--) L(bottom[i]);
        sb.Append(" Z M0,0 L0.001,0 L0.001,100 L0,100 Z");
        return sb.ToString();
    }

    public static string KindName(MediaKind kind) => kind switch
    {
        MediaKind.Video => "Vídeo",
        MediaKind.Audio => "Áudio",
        MediaKind.Image => "Imagem",
        MediaKind.ImageSequence => "Sequência de imagens",
        MediaKind.AnimatedGif => "GIF animado",
        _ => "Mídia",
    };

    public static string FormatDuration(MediaTime t)
    {
        var total = TimeSpan.FromSeconds(Math.Max(0, t.TotalSeconds));
        return total.TotalHours >= 1 ? total.ToString(@"h\:mm\:ss", PtBr) : total.ToString(@"m\:ss", PtBr);
    }

    private static string? AudioSummary(MediaTechnicalInfo? i)
    {
        if (i is null || i.SampleRate <= 0) return null;
        var channels = i.Channels switch { 1 => "mono", 2 => "estéreo", 6 => "5.1", 8 => "7.1", var n => $"{n} canais" };
        return $"{(i.SampleRate / 1000.0).ToString("0.#", PtBr)} kHz · {channels}";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{(bytes / (double)(1L << 30)).ToString("0.##", PtBr)} GB",
        >= 1L << 20 => $"{(bytes / (double)(1L << 20)).ToString("0.#", PtBr)} MB",
        >= 1L << 10 => $"{(bytes / 1024.0).ToString("0", PtBr)} KB",
        _ => $"{bytes} bytes",
    };

    private static string FormatBitRate(long bps) => bps >= 1_000_000
        ? $"{(bps / 1_000_000.0).ToString("0.##", PtBr)} Mbit/s"
        : $"{(bps / 1000.0).ToString("0", PtBr)} kbit/s";
}
