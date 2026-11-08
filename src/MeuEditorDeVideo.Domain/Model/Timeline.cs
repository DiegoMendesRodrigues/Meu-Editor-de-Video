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

    /// <summary>Conteúdo textual (clipes de texto/legenda).</summary>
    public string? Text { get; init; }

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
    public ImmutableList<Clip> Clips { get; init; } = [];
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
