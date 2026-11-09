using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Model;

public enum TrackKind
{
    Video,
    Audio,
    Image,
    Text,
    Subtitle,
    Effect,
}

/// <summary>Transformações espaciais de um clipe (RF065–RF069). Posição em pixels relativa ao centro do quadro.</summary>
public sealed record Transform
{
    public static readonly Transform Identity = new();

    public double X { get; init; }
    public double Y { get; init; }
    public double ScaleX { get; init; } = 1;
    public double ScaleY { get; init; } = 1;
    public double RotationDegrees { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    public double CropLeft { get; init; }
    public double CropTop { get; init; }
    public double CropRight { get; init; }
    public double CropBottom { get; init; }
}

/// <summary>Clipe na timeline (§6.3).</summary>
public sealed record Clip
{
    public required Guid Id { get; init; }

    /// <summary>Mídia de origem; nulo para clipes gerados (texto, cor sólida etc.).</summary>
    public Guid? MediaAssetId { get; init; }

    /// <summary>Posição inicial na timeline.</summary>
    public required MediaTime Start { get; init; }

    /// <summary>Duração na timeline (já considerando a velocidade).</summary>
    public required MediaTime Duration { get; init; }

    /// <summary>Ponto inicial dentro da mídia de origem.</summary>
    public MediaTime SourceIn { get; init; }

    public Rational Speed { get; init; } = Rational.One;
    public double Opacity { get; init; } = 1;
    public double Volume { get; init; } = 1;
    public Transform Transform { get; init; } = Transform.Identity;
    public Guid? GroupId { get; init; }

    /// <summary>
    /// Clipes com o mesmo LinkId (ex.: vídeo e áudio da mesma mídia) são movidos, aparados e divididos juntos,
    /// preservando a sincronização (RF059, RF064).
    /// </summary>
    public Guid? LinkId { get; init; }

    /// <summary>Conteúdo textual (clipes de texto/legenda).</summary>
    public string? Text { get; init; }

    /// <summary>Formatação do texto (estilo, posição, animações); nula = padrão (RF136–RF139).</summary>
    public TextFormat? TextFormat { get; init; }

    /// <summary>Clipe gerado de texto/título/créditos/tarja/legenda.</summary>
    public bool IsText => Text is not null && MediaAssetId is null;

    /// <summary>Efeitos, correção de cor e chroma key, aplicados em ordem (RF077, RF088).</summary>
    public ImmutableList<EffectInstance> Effects { get; init; } = [];

    /// <summary>Modo de mistura com as camadas abaixo (RF151).</summary>
    public BlendMode BlendMode { get; init; }

    /// <summary>Keyframes por propriedade animada (<see cref="AnimatedProperty"/>); offsets relativos ao início do clipe.</summary>
    public ImmutableDictionary<string, ImmutableList<Keyframe>> Keyframes { get; init; } = ImmutableDictionary<string, ImmutableList<Keyframe>>.Empty;

    /// <summary>Congelamento de quadro (RF073): instante fixo da mídia mostrado durante todo o clipe.</summary>
    public MediaTime? FreezeAt { get; init; }

    /// <summary>Câmera lenta suavizada: mistura quadros vizinhos quando a velocidade é menor que 1 (RF071).</summary>
    public bool FrameBlending { get; init; }

    /// <summary>Entrada gradual a partir do transparente (RF079).</summary>
    public MediaTime FadeIn { get; init; }

    /// <summary>Saída gradual para o transparente (RF079).</summary>
    public MediaTime FadeOut { get; init; }

    /// <summary>Com velocidade diferente de 1×, mantém o tom original da voz/música (esticamento de tempo).</summary>
    public bool PreservePitch { get; init; } = true;

    public MediaTime End => Start + Duration;

    /// <summary>Ponto final dentro da mídia de origem.</summary>
    public MediaTime SourceOut => SourceIn + Speed.SourceDurationOf(Duration);
}

public sealed record Track
{
    public required Guid Id { get; init; }
    public required TrackKind Kind { get; init; }
    public required string Name { get; init; }
    public bool IsLocked { get; init; }
    public bool IsHidden { get; init; }
    public bool IsMuted { get; init; }

    /// <summary>Faixa solo: com alguma trilha em solo, só as trilhas em solo soam (mixer, RF130).</summary>
    public bool IsSolo { get; init; }

    /// <summary>Volume da trilha no mixer (1 = 0 dB; até 4 = +12 dB).</summary>
    public double Volume { get; init; } = 1;

    /// <summary>Panorâmica: −1 = esquerda, 0 = centro, 1 = direita.</summary>
    public double Pan { get; init; }

    public ImmutableList<Clip> Clips { get; init; } = [];

    /// <summary>Transições entre clipes adjacentes desta trilha (RF078–RF085).</summary>
    public ImmutableList<Transition> Transitions { get; init; } = [];

    /// <summary>Tipos de trilha que exibem imagem (vídeo, imagens, textos, legendas, efeitos).</summary>
    public bool IsVisual => Kind != TrackKind.Audio;
}

public sealed record Marker
{
    public required Guid Id { get; init; }
    public required MediaTime Time { get; init; }
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#3D9BFF";
}

public sealed record Sequence
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public ImmutableList<Track> Tracks { get; init; } = [];
    public ImmutableList<Marker> Markers { get; init; } = [];

    public MediaTime Duration =>
        Tracks.SelectMany(t => t.Clips).Select(c => c.End).DefaultIfEmpty(MediaTime.Zero).Max();
}
