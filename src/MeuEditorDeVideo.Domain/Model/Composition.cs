using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Model;

/// <summary>Forma de uma máscara (RF110).</summary>
public enum MaskKind
{
    Rectangle,
    Ellipse,

    /// <summary>Contorno livre por pontos (opcionalmente suavizado).</summary>
    Path,
}

/// <summary>Como a máscara se combina com as anteriores da lista.</summary>
public enum MaskMode
{
    /// <summary>Soma (união) com as máscaras anteriores.</summary>
    Add,

    /// <summary>Recorta (subtrai) a área das máscaras anteriores.</summary>
    Subtract,

    /// <summary>Mantém só a interseção com as máscaras anteriores.</summary>
    Intersect,
}

/// <summary>Ponto normalizado (0…1) nas coordenadas da imagem de origem da camada.</summary>
public readonly record struct MaskPoint(double X, double Y);

/// <summary>Forma da máscara num instante (máscara animada, RF149); offset relativo ao início do clipe.</summary>
public sealed record MaskShapeKey(MediaTime Offset, ImmutableList<MaskPoint> Points, Easing Easing = Easing.EaseInOut);

/// <summary>
/// Máscara de recorte de uma camada (RF110, RF149, RF111). Os pontos são normalizados na imagem de origem:
/// retângulo/elipse usam dois pontos (canto superior esquerdo e inferior direito); o contorno livre usa 3+ pontos.
/// </summary>
public sealed record Mask
{
    public required Guid Id { get; init; }
    public string Name { get; init; } = "Máscara";
    public MaskKind Kind { get; init; } = MaskKind.Rectangle;
    public ImmutableList<MaskPoint> Points { get; init; } = [];

    /// <summary>Contorno livre suavizado (curva que passa pelos pontos).</summary>
    public bool Smooth { get; init; }

    /// <summary>Suavização da borda, em fração da largura da imagem (0…0,25).</summary>
    public double Feather { get; init; }

    /// <summary>Contração (positivo) ou expansão (negativo) da área, em fração da largura da imagem.</summary>
    public double Expansion { get; init; }

    public bool Invert { get; init; }
    public MaskMode Mode { get; init; } = MaskMode.Add;
    public double Opacity { get; init; } = 1;
    public bool Enabled { get; init; } = true;

    /// <summary>Formas-chave da máscara animada; vazia = forma fixa (<see cref="Points"/>).</summary>
    public ImmutableList<MaskShapeKey> Keys { get; init; } = [];

    /// <summary>Rastreamento do clipe que a máscara segue (RF111); nulo = parada.</summary>
    public Guid? TrackerId { get; init; }

    /// <summary>Instante de origem em que a máscara foi posicionada sobre o objeto rastreado.</summary>
    public MediaTime TrackerReference { get; init; }
}

/// <summary>Posição do objeto rastreado num quadro de origem: centro e tamanho normalizados (0…1) na imagem.</summary>
public sealed record TrackSample(MediaTime SourceTime, double X, double Y, double Width, double Height);

/// <summary>Rastreamento de movimento de uma região do clipe (RF150), calculado por visão computacional.</summary>
public sealed record MotionTracker
{
    public required Guid Id { get; init; }
    public string Name { get; init; } = "Rastreamento";

    /// <summary>Região inicial (centro e tamanho normalizados) e instante de origem onde foi marcada.</summary>
    public required TrackSample Region { get; init; }

    /// <summary>Amostras por quadro de origem, em ordem de tempo.</summary>
    public ImmutableList<TrackSample> Samples { get; init; } = [];
}

/// <summary>
/// Vínculo de um elemento ao movimento rastreado de outro clipe (RF150): a posição (e, opcionalmente, a escala)
/// acompanha o deslocamento do objeto desde o instante de referência.
/// </summary>
public sealed record TrackerLink
{
    public required Guid ClipId { get; init; }
    public required Guid TrackerId { get; init; }

    /// <summary>Instante da timeline em que o elemento foi posicionado (deslocamento zero).</summary>
    public required MediaTime ReferenceTime { get; init; }

    public bool FollowScale { get; init; }
}

/// <summary>Movimento da câmera entre um quadro e o anterior, normalizado pela largura da imagem (dx, dy) e em radianos (da).</summary>
public sealed record MotionStep(MediaTime SourceTime, double Dx, double Dy, double Da);

/// <summary>Estabilização de vídeo (RF075): movimento medido por quadro e intensidade da suavização.</summary>
public sealed record Stabilization
{
    public bool Enabled { get; init; } = true;

    /// <summary>Raio da suavização da trajetória, em segundos (maior = câmera mais estável).</summary>
    public double Smoothness { get; init; } = 1;

    /// <summary>Ampliação extra para esconder as bordas deslocadas (0 = automática).</summary>
    public double Zoom { get; init; }

    /// <summary>Movimento quadro a quadro na mídia de origem, em ordem de tempo.</summary>
    public ImmutableList<MotionStep> Steps { get; init; } = [];
}

/// <summary>Formas prontas dos elementos gráficos (RF152).</summary>
public enum ShapeKind
{
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Star,
    Polygon,
    SpeechBubble,
    Heart,
}

/// <summary>Elemento gráfico vetorial gerado (sem mídia), desenhado na resolução de saída.</summary>
public sealed record ShapeStyle
{
    public ShapeKind Kind { get; init; } = ShapeKind.Rectangle;

    /// <summary>Tamanho em pixels do projeto.</summary>
    public double Width { get; init; } = 400;

    public double Height { get; init; } = 240;
    public string FillColor { get; init; } = "#FF3D9BFF";
    public string StrokeColor { get; init; } = "#FFFFFFFF";
    public double StrokeWidth { get; init; }
    public double CornerRadius { get; init; }

    /// <summary>Pontas da estrela / lados do polígono.</summary>
    public int Sides { get; init; } = 5;
}
