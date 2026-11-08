using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.TimelineEngine;

public enum SnapKind
{
    None,
    ClipEdge,
    Playhead,
    Marker,
    Zero,
}

/// <summary>Resultado do alinhamento: tempo final e o alvo encontrado (para desenhar a guia).</summary>
public readonly record struct SnapResult(MediaTime Time, SnapKind Kind, MediaTime? Target)
{
    public bool Snapped => Kind != SnapKind.None;
}

/// <summary>
/// Ímã de alinhamento (RF054): atrai tempos para bordas de clipes, cursor de reprodução, marcadores e o início,
/// dentro de uma tolerância (normalmente alguns pixels convertidos em tempo). Sem alvo, alinha à grade de quadros.
/// </summary>
public sealed class SnapEngine
{
    private readonly List<(MediaTime Time, SnapKind Kind)> _targets = [];
    private readonly FrameRate _frameRate;

    public SnapEngine(Sequence sequence, FrameRate frameRate, MediaTime playhead, IReadOnlySet<Guid>? excludeClips = null)
    {
        _frameRate = frameRate;
        _targets.Add((MediaTime.Zero, SnapKind.Zero));
        _targets.Add((playhead, SnapKind.Playhead));
        foreach (var m in sequence.Markers) _targets.Add((m.Time, SnapKind.Marker));
        foreach (var c in sequence.Tracks.SelectMany(t => t.Clips))
        {
            if (excludeClips?.Contains(c.Id) == true) continue;
            _targets.Add((c.Start, SnapKind.ClipEdge));
            _targets.Add((c.End, SnapKind.ClipEdge));
        }
    }

    public int TargetCount => _targets.Count;

    /// <summary>Alinha um único instante.</summary>
    public SnapResult Snap(MediaTime time, MediaTime tolerance, bool enabled = true)
    {
        if (enabled && Nearest(time, tolerance) is { } hit)
            return new SnapResult(hit.Time, hit.Kind, hit.Time);
        return new SnapResult(_frameRate.SnapToFrame(time), SnapKind.None, null);
    }

    /// <summary>
    /// Alinha um intervalo (ex.: clipe sendo arrastado): testa início e fim e aplica o menor deslocamento.
    /// Retorna o novo início.
    /// </summary>
    public SnapResult SnapRange(MediaTime start, MediaTime duration, MediaTime tolerance, bool enabled = true)
    {
        if (enabled)
        {
            var a = Nearest(start, tolerance);
            var b = Nearest(start + duration, tolerance);
            var da = a is { } x ? Abs(x.Time - start) : MediaTime.MaxValue;
            var db = b is { } y ? Abs(y.Time - (start + duration)) : MediaTime.MaxValue;
            if (a is { } ha && da <= db) return new SnapResult(ha.Time, ha.Kind, ha.Time);
            if (b is { } hb) return new SnapResult(hb.Time - duration, hb.Kind, hb.Time);
        }
        return new SnapResult(_frameRate.SnapToFrame(start), SnapKind.None, null);
    }

    private (MediaTime Time, SnapKind Kind)? Nearest(MediaTime time, MediaTime tolerance)
    {
        (MediaTime Time, SnapKind Kind)? best = null;
        var bestDistance = MediaTime.MaxValue;
        foreach (var t in _targets)
        {
            var d = Abs(t.Time - time);
            if (d > tolerance) continue;
            // Em empate, prioriza cursor e marcadores em relação a bordas.
            if (d < bestDistance || (d == bestDistance && best is { } b && Priority(t.Kind) > Priority(b.Kind)))
            {
                best = t;
                bestDistance = d;
            }
        }
        return best;
    }

    private static int Priority(SnapKind k) => k switch
    {
        SnapKind.Playhead => 3,
        SnapKind.Marker => 2,
        SnapKind.ClipEdge => 1,
        _ => 0,
    };

    private static MediaTime Abs(MediaTime t) => t < MediaTime.Zero ? -t : t;
}
