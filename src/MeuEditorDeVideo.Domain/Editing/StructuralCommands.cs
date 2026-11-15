using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using static MeuEditorDeVideo.Domain.Editing.TimelineOps;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>
/// Camada de ajuste: clipe sem mídia numa trilha de efeitos; os efeitos dele valem para todas as camadas abaixo. Entra na
/// primeira trilha de efeitos livre no intervalo ou numa trilha nova no topo.
/// </summary>
public sealed record AddAdjustmentLayerCommand(Guid SequenceId, MediaTime At, MediaTime Duration) : IEditCommand
{
    public string Description => "Adicionar camada de ajuste";

    public Guid ClipId { get; init; } = Guid.NewGuid();

    public Guid NewTrackId { get; init; } = Guid.NewGuid();

    public Project Apply(Project project)
    {
        if (Duration <= MediaTime.Zero) throw new EditRejectedException("A camada de ajuste precisa de duração.");
        var seq = RequireSequence(project, SequenceId);
        var start = project.Settings.FrameRate.SnapToFrame(MediaTime.Max(At, MediaTime.Zero));
        var clip = new Clip { Id = ClipId, Start = start, Duration = Duration, IsAdjustment = true, LabelColor = "#8E7CC3" };
        bool Free(Track t) => t.Kind == TrackKind.Effect && !t.IsLocked && !t.Clips.Exists(c => c.Start < clip.End && c.End > clip.Start);
        var target = seq.Tracks.Find(Free);
        if (target is null)
        {
            var n = seq.Tracks.Count(t => t.Kind == TrackKind.Effect) + 1;
            target = new Track { Id = NewTrackId, Kind = TrackKind.Effect, Name = $"Ajuste {n}" };
            // Acima das mídias, mas abaixo de textos e legendas (que são compostos por último de qualquer forma).
            seq = seq with { Tracks = seq.Tracks.Insert(0, target) };
        }
        return Commit(project, ReplaceTrack(seq, Place(target, clip)));
    }
}

/// <summary>Ativa ou desativa clipes (desativado não aparece nem soa).</summary>
public sealed record SetClipsEnabledCommand(Guid SequenceId, IReadOnlyList<Guid> ClipIds, bool Enabled) : IEditCommand
{
    public string Description => Enabled ? "Ativar clipe" : "Desativar clipe";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var ids = ClipIds.ToHashSet();
        var changed = false;
        foreach (var track in seq.Tracks)
        {
            if (!track.Clips.Exists(c => ids.Contains(c.Id))) continue;
            EnsureUnlocked(track);
            seq = ReplaceTrack(seq, track with { Clips = [.. track.Clips.Select(c => ids.Contains(c.Id) ? c with { Enabled = Enabled } : c)] });
            changed = true;
        }
        if (!changed) throw new EditRejectedException("Clipe não encontrado.");
        return Commit(project, seq);
    }
}

/// <summary>
/// Multicâmera: no instante <paramref name="At"/>, corta os clipes de vídeo das trilhas de vídeo (cada trilha é uma câmera,
/// de cima para baixo) e deixa visível só a câmera <paramref name="Angle"/> (0 = primeira) a partir dali, até o fim dos
/// trechos. O áudio não muda. As câmeras precisam estar sincronizadas (por exemplo, com "Sincronizar pelo áudio").
/// </summary>
public sealed record MulticamSwitchCommand(Guid SequenceId, int Angle, MediaTime At) : IEditCommand
{
    public string Description => $"Multicâmera: câmera {Angle + 1}";

    /// <summary>Câmeras no instante: trilhas de vídeo desbloqueadas e visíveis com um clipe de mídia no cursor, de cima para baixo.</summary>
    public static IReadOnlyList<(Track Track, Clip Clip)> Cameras(Sequence seq, MediaTime at) =>
        [.. seq.Tracks.Where(t => t.Kind == TrackKind.Video && !t.IsLocked && !t.IsHidden)
            .Select(t => (Track: t, Clip: t.Clips.Find(c => c.Start <= at && at < c.End && c.MediaAssetId is not null)))
            .Where(x => x.Clip is not null)
            .Select(x => (x.Track, x.Clip!))];

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var at = project.Settings.FrameRate.SnapToFrame(At);
        var cams = Cameras(seq, at);
        if (cams.Count < 2) throw new EditRejectedException("A multicâmera precisa de clipes de vídeo em pelo menos duas trilhas de vídeo no cursor.");
        if (Angle < 0 || Angle >= cams.Count) throw new EditRejectedException($"Não há a câmera {Angle + 1} no cursor (há {cams.Count}).");
        foreach (var (i, (track, clip)) in cams.Index())
        {
            var current = RequireTrack(seq, track.Id);
            var piece = clip;
            var clips = current.Clips;
            if (at > clip.Start && Split(clip, at, Guid.NewGuid(), null) is { } parts)
            {
                piece = parts.Right;
                clips = clips.Replace(clip, parts.Left).Add(parts.Right);
            }
            var enabled = i == Angle;
            if (piece.Enabled != enabled || piece != clip)
                clips = clips.Replace(piece, piece with { Enabled = enabled });
            seq = ReplaceTrack(seq, WithClips(current, clips));
        }
        return Commit(project, seq);
    }
}

/// <summary>Modelo de tela dividida: células em frações do quadro (x, y, largura, altura).</summary>
public sealed record SplitScreenLayout(string Id, string Name, IReadOnlyList<(double X, double Y, double W, double H)> Cells);

public static class SplitScreenLayouts
{
    public static IReadOnlyList<SplitScreenLayout> All { get; } =
    [
        new("2h", "2 lado a lado", [(0, 0, 0.5, 1), (0.5, 0, 0.5, 1)]),
        new("2v", "2 empilhados", [(0, 0, 1, 0.5), (0, 0.5, 1, 0.5)]),
        new("3h", "3 colunas", [(0, 0, 1 / 3.0, 1), (1 / 3.0, 0, 1 / 3.0, 1), (2 / 3.0, 0, 1 / 3.0, 1)]),
        new("3v", "3 faixas", [(0, 0, 1, 1 / 3.0), (0, 1 / 3.0, 1, 1 / 3.0), (0, 2 / 3.0, 1, 1 / 3.0)]),
        new("1+2", "1 grande e 2 pequenos", [(0, 0, 0.5, 1), (0.5, 0, 0.5, 0.5), (0.5, 0.5, 0.5, 0.5)]),
        new("4", "4 em grade", [(0, 0, 0.5, 0.5), (0.5, 0, 0.5, 0.5), (0, 0.5, 0.5, 0.5), (0.5, 0.5, 0.5, 0.5)]),
    ];

    public static SplitScreenLayout? Find(string id) => All.FirstOrDefault(l => l.Id == id);
}

/// <summary>
/// Tela dividida: cada clipe (em ordem das trilhas, de cima para baixo) preenche uma célula do modelo, ampliado para cobrir a
/// célula e recortado no excesso. Posição, escala, rotação e recorte animados são substituídos. Os clipes ficam em trilhas
/// diferentes para aparecerem juntos.
/// </summary>
public sealed record SplitScreenCommand(Guid SequenceId, IReadOnlyList<Guid> ClipIds, string LayoutId) : IEditCommand
{
    public string Description => "Tela dividida";

    private static readonly string[] Reset = [AnimatedProperty.X, AnimatedProperty.Y, AnimatedProperty.Scale, AnimatedProperty.Rotation];

    public Project Apply(Project project)
    {
        var layout = SplitScreenLayouts.Find(LayoutId) ?? throw new EditRejectedException("Modelo de tela dividida desconhecido.");
        var seq = RequireSequence(project, SequenceId);
        var refs = ClipIds.Distinct().Select(id => Find(seq, id)).OfType<ClipRef>()
            .Where(r => r.Track.IsVisual && r.Clip.MediaAssetId is { } m && project.FindMedia(m) is { HasVideo: true, Width: > 0, Height: > 0 })
            .OrderBy(r => r.TrackIndex).ThenBy(r => r.Clip.Start).Take(layout.Cells.Count).ToList();
        if (refs.Count < 2) throw new EditRejectedException("Selecione pelo menos dois clipes de vídeo ou imagem.");
        if (refs.Select(r => r.Track.Id).Distinct().Count() < refs.Count)
            throw new EditRejectedException("Coloque cada clipe numa trilha diferente para que apareçam juntos.");

        double pw = project.Settings.Width, ph = project.Settings.Height;
        foreach (var (i, r) in refs.Index())
        {
            EnsureUnlocked(r.Track);
            var asset = project.FindMedia(r.Clip.MediaAssetId!.Value)!;
            var transform = CellTransform(asset.Width, asset.Height, pw, ph, layout.Cells[i]);
            var keyframes = r.Clip.Keyframes;
            foreach (var p in Reset) keyframes = keyframes.Remove(p);
            var updated = r.Clip with { Transform = transform, Keyframes = keyframes, Stabilization = null, MotionLink = null };
            var track = RequireTrack(seq, r.Track.Id);
            seq = ReplaceTrack(seq, track with { Clips = track.Clips.Replace(track.Clips.Find(c => c.Id == r.Clip.Id)!, updated) });
        }
        return Commit(project, seq);
    }

    /// <summary>Transformação que faz a mídia (ajustada ao quadro, "contain") cobrir a célula, recortando o excesso por igual.</summary>
    public static Transform CellTransform(int mediaWidth, int mediaHeight, double projectWidth, double projectHeight, (double X, double Y, double W, double H) cell)
    {
        var fit = Math.Min(projectWidth / mediaWidth, projectHeight / mediaHeight);
        double fw = mediaWidth * fit, fh = mediaHeight * fit;
        double cw = cell.W * projectWidth, ch = cell.H * projectHeight;
        var scale = Math.Max(cw / fw, ch / fh);
        var cropX = Math.Max(0, (1 - cw / (fw * scale)) / 2);
        var cropY = Math.Max(0, (1 - ch / (fh * scale)) / 2);
        return Transform.Identity with
        {
            X = (cell.X + cell.W / 2 - 0.5) * projectWidth,
            Y = (cell.Y + cell.H / 2 - 0.5) * projectHeight,
            ScaleX = scale,
            ScaleY = scale,
            CropLeft = cropX,
            CropRight = cropX,
            CropTop = cropY,
            CropBottom = cropY,
        };
    }
}

/// <summary>
/// Cria um clipe composto: os clipes selecionados (e os vinculados) saem da sequência e vão para uma sequência nova, com as
/// mesmas trilhas e posições relativas; no lugar entra um único clipe que mostra e toca essa sequência.
/// </summary>
public sealed record CreateCompoundClipCommand(Guid SequenceId, IReadOnlyList<Guid> ClipIds, string Name = "Clipe composto") : IEditCommand
{
    public string Description => "Criar clipe composto";

    public Guid NewSequenceId { get; init; } = Guid.NewGuid();

    public Guid NewClipId { get; init; } = Guid.NewGuid();

    public Guid NewTrackId { get; init; } = Guid.NewGuid();

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var ids = Expand(seq, ClipIds, linked: true, groups: true);
        var moving = seq.Tracks.SelectMany((t, i) => t.Clips.Where(c => ids.Contains(c.Id)).Select(c => (Index: i, Track: t, Clip: c))).ToList();
        if (moving.Count == 0) throw new EditRejectedException("Selecione os clipes para o clipe composto.");
        foreach (var t in moving.Select(m => m.Track).Distinct()) EnsureUnlocked(t);
        var start = moving.Min(m => m.Clip.Start);
        var end = moving.Max(m => m.Clip.End);

        // Sequência aninhada: mesmas trilhas (na mesma ordem), clipes deslocados para começar em zero.
        var nestedTracks = new List<Track>();
        foreach (var group in moving.GroupBy(m => m.Index).OrderBy(g => g.Key))
        {
            var track = group.First().Track;
            var clips = group.Select(m => m.Clip with { Start = m.Clip.Start - start }).ToList();
            var movedIds = clips.Select(c => c.Id).ToHashSet();
            nestedTracks.Add(new Track
            {
                Id = Guid.NewGuid(),
                Kind = track.Kind,
                Name = track.Name,
                IsMuted = track.IsMuted,
                Volume = track.Volume,
                Pan = track.Pan,
                Clips = [.. clips.OrderBy(c => c.Start)],
                Transitions = [.. track.Transitions.Where(tr => movedIds.Contains(tr.FromClipId) && movedIds.Contains(tr.ToClipId))],
            });
            var remaining = track.Clips.Where(c => !movedIds.Contains(c.Id)).ToList();
            seq = ReplaceTrack(seq, WithClips(track with
            {
                Transitions = [.. track.Transitions.Where(tr => !movedIds.Contains(tr.FromClipId) && !movedIds.Contains(tr.ToClipId))],
            }, remaining));
        }
        var nested = new Sequence { Id = NewSequenceId, Name = Name, Tracks = [.. nestedTracks] };

        var compound = new Clip { Id = NewClipId, Start = start, Duration = end - start, NestedSequenceId = NewSequenceId, LabelColor = "#C27C0E" };
        // Entra na trilha visual mais alta usada (se ficou livre no intervalo) ou numa trilha de vídeo nova no topo.
        var target = moving.Where(m => m.Track.Kind is TrackKind.Video or TrackKind.Image).OrderBy(m => m.Index)
            .Select(m => RequireTrack(seq, m.Track.Id)).FirstOrDefault(t => !t.Clips.Exists(c => c.Start < end && c.End > start));
        if (target is null)
        {
            target = new Track { Id = NewTrackId, Kind = TrackKind.Video, Name = Name };
            var firstVisual = seq.Tracks.FindIndex(t => t.Kind is TrackKind.Video or TrackKind.Image);
            seq = seq with { Tracks = seq.Tracks.Insert(firstVisual < 0 ? 0 : firstVisual, target) };
        }
        seq = ReplaceTrack(seq, Place(target, compound));
        return (project with { Sequences = project.Sequences.Add(nested) }).WithSequence(seq);
    }
}

/// <summary>
/// Desfaz um clipe composto: os clipes da sequência aninhada voltam para a linha do tempo (em trilhas novas logo acima da
/// trilha do clipe composto), aparados ao trecho que o clipe mostrava. A sequência aninhada é apagada se ninguém mais a usar.
/// </summary>
public sealed record UnpackCompoundClipCommand(Guid SequenceId, Guid ClipId) : IEditCommand
{
    public string Description => "Desfazer clipe composto";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var r = Require(seq, ClipId);
        if (!r.Clip.IsCompound) throw new EditRejectedException("O clipe não é um clipe composto.");
        EnsureUnlocked(r.Track);
        if (r.Clip.Speed != Rational.One || r.Clip.Reverse) throw new EditRejectedException("Volte o clipe composto à velocidade normal antes de desfazê-lo.");
        var nested = project.Sequences.Find(s => s.Id == r.Clip.NestedSequenceId) ?? throw new EditRejectedException("A sequência do clipe composto não existe.");

        var from = r.Clip.SourceIn;
        var to = r.Clip.SourceIn + r.Clip.Duration;
        var delta = r.Clip.Start - from;
        var links = new Dictionary<Guid, Guid>();
        Guid? Remap(Guid? id)
        {
            if (id is not { } v) return null;
            if (!links.TryGetValue(v, out var n)) links[v] = n = Guid.NewGuid();
            return n;
        }

        var newTracks = new List<Track>();
        foreach (var track in nested.Tracks)
        {
            var ids = new Dictionary<Guid, Guid>();
            var clips = new List<Clip>();
            foreach (var c in track.Clips)
            {
                if (c.End <= from || c.Start >= to) continue;
                var clip = c;
                if (clip.Start < from) clip = TrimStartTo(clip, from);
                if (clip.End > to) clip = TrimEndTo(clip, to);
                var id = ids[c.Id] = Guid.NewGuid();
                clips.Add(clip with { Id = id, Start = clip.Start + delta, LinkId = Remap(clip.LinkId), GroupId = Remap(clip.GroupId) });
            }
            if (clips.Count == 0) continue;
            newTracks.Add(track with
            {
                Id = Guid.NewGuid(),
                Clips = [.. clips.OrderBy(c => c.Start)],
                Transitions = [.. track.Transitions.Where(t => ids.ContainsKey(t.FromClipId) && ids.ContainsKey(t.ToClipId))
                    .Select(t => t with { FromClipId = ids[t.FromClipId], ToClipId = ids[t.ToClipId] })],
            });
        }

        var owner = RequireTrack(seq, r.Track.Id);
        seq = ReplaceTrack(seq, owner with
        {
            Clips = owner.Clips.Remove(r.Clip),
            Transitions = [.. owner.Transitions.Where(t => t.FromClipId != r.Clip.Id && t.ToClipId != r.Clip.Id)],
        });
        // Nomes únicos: as trilhas originais costumam continuar na sequência (vazias) com os mesmos nomes.
        var names = seq.Tracks.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < newTracks.Count; i++)
        {
            var name = newTracks[i].Name;
            for (var n = 2; names.Contains(name); n++) name = $"{newTracks[i].Name} ({n})";
            names.Add(name);
            newTracks[i] = newTracks[i] with { Name = name };
        }
        var at = seq.Tracks.FindIndex(t => t.Id == r.Track.Id);
        seq = seq with { Tracks = seq.Tracks.InsertRange(at, newTracks.Select(TransitionOps.Normalize)) };

        var result = project.WithSequence(seq);
        var stillUsed = result.Sequences.SelectMany(s => s.Tracks).SelectMany(t => t.Clips).Any(c => c.NestedSequenceId == nested.Id);
        if (!stillUsed && result.Sequences.Count > 1 && result.MainSequence.Id != nested.Id)
            result = result with { Sequences = result.Sequences.Remove(result.Sequences.Find(s => s.Id == nested.Id)!) };
        return result;
    }
}

/// <summary>
/// Insere uma mídia sobre o vídeo (figurinhas, gravações): no cursor, na primeira trilha visual compatível livre no intervalo
/// (imagens em trilhas de imagem, vídeos em trilhas de vídeo) ou numa trilha nova no topo. O áudio vinculado vai para uma trilha
/// de áudio livre ou nova.
/// </summary>
public sealed record AddOverlayCommand(Guid SequenceId, Guid AssetId, MediaTime At, MediaTime ImageDuration, string TrackName = "Sobreposição") : IEditCommand
{
    public string Description => "Inserir sobreposição";

    public Guid VideoClipId { get; init; } = Guid.NewGuid();

    /// <summary>Escala inicial do clipe (figurinhas entram menores que o quadro).</summary>
    public double Scale { get; init; } = 1;

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var asset = project.FindMedia(AssetId) ?? throw new EditRejectedException("Mídia não encontrada.");
        if (!asset.HasVideo) throw new EditRejectedException("A mídia não tem imagem.");
        var fr = project.Settings.FrameRate;
        var start = fr.SnapToFrame(MediaTime.Max(At, MediaTime.Zero));
        var end = start + Frames.Duration(fr, asset.Kind == MediaKind.Image ? ImageDuration : asset.Duration);
        var kind = asset.Kind == MediaKind.Video ? TrackKind.Video : TrackKind.Image;
        bool Free(Track t, TrackKind k) => t.Kind == k && !t.IsLocked && !t.Clips.Exists(c => c.Start < end && c.End > start);

        var video = seq.Tracks.Find(t => Free(t, kind));
        if (video is null)
        {
            video = new Track { Id = Guid.NewGuid(), Kind = kind, Name = TrackName };
            var firstVisual = seq.Tracks.FindIndex(t => t.Kind is TrackKind.Video or TrackKind.Image or TrackKind.Effect);
            seq = seq with { Tracks = seq.Tracks.Insert(firstVisual < 0 ? 0 : firstVisual, video) };
        }
        Guid? audio = null;
        if (asset.HasAudio && asset.Kind == MediaKind.Video)
        {
            var a = seq.Tracks.Find(t => Free(t, TrackKind.Audio));
            if (a is null)
            {
                a = new Track { Id = Guid.NewGuid(), Kind = TrackKind.Audio, Name = TrackName };
                seq = seq with { Tracks = seq.Tracks.Add(a) };
            }
            audio = a.Id;
        }
        var result = new AddMediaToTimelineCommand(SequenceId, AssetId, video.Id, audio, start, ImageDuration) { VideoClipId = VideoClipId, Description = Description }
            .Apply(project.WithSequence(seq));
        if (Scale == 1) return result;
        var s2 = RequireSequence(result, SequenceId);
        var t2 = RequireTrack(s2, video.Id);
        var clip = t2.Clips.Find(c => c.Id == VideoClipId)!;
        var scaled = clip with { Transform = clip.Transform with { ScaleX = Scale, ScaleY = Scale } };
        return result.WithSequence(ReplaceTrack(s2, t2 with { Clips = t2.Clips.Replace(clip, scaled) }));
    }
}
