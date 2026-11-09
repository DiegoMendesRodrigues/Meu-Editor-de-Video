using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using static MeuEditorDeVideo.Domain.Editing.TimelineOps;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Modelos prontos de texto (RF132–RF135, RF143), proporcionais à altura do projeto (referência 1080p).</summary>
public static class TextPresets
{
    public static readonly MediaTime DefaultDuration = MediaTime.FromSeconds(5);
    public static readonly MediaTime CreditsDuration = MediaTime.FromSeconds(12);

    public const string CreditsSample =
        "CRÉDITOS\n\nDireção\nNome Sobrenome\n\nEdição\nNome Sobrenome\n\nTrilha sonora\nNome Sobrenome\n\nObrigado por assistir!";

    /// <summary>Trilha onde o modelo é inserido.</summary>
    public static TrackKind TrackKindFor(TextKind kind) => kind == TextKind.Subtitle ? TrackKind.Subtitle : TrackKind.Text;

    /// <summary>Formato padrão das legendas: caixa escura translúcida, centralizada perto da base.</summary>
    public static TextFormat Subtitle(ProjectSettings settings)
    {
        var k = settings.Height / 1080.0;
        return new TextFormat
        {
            Kind = TextKind.Subtitle,
            Anchor = TextAnchor.Bottom,
            Style = new TextStyle
            {
                FontSize = Math.Round(52 * k), MaxWidth = 0.8, BackgroundColor = "#A0000000",
                BackgroundPadding = Math.Round(12 * k), BackgroundRadius = Math.Round(6 * k),
            },
        };
    }

    /// <summary>Posição vertical das legendas (pixels a partir do centro).</summary>
    public static double SubtitleY(ProjectSettings settings) => Math.Round(settings.Height / 2.0 - settings.Height * 0.07);

    /// <summary>Clipe do modelo, começando em <paramref name="at"/>.</summary>
    public static Clip Create(TextKind kind, ProjectSettings settings, MediaTime at)
    {
        var k = settings.Height / 1080.0;
        double W = settings.Width, H = settings.Height;
        var fade = MediaTime.FromSecondsApprox(0.4);
        var (text, format, transform, duration) = kind switch
        {
            TextKind.Title => ("Título", new TextFormat
            {
                Kind = kind,
                Style = new TextStyle { FontSize = Math.Round(140 * k), Bold = true, Shadow = true, ShadowBlur = Math.Round(12 * k), ShadowOffsetX = 0, ShadowOffsetY = Math.Round(6 * k) },
                AnimationIn = TextAnimationKind.Zoom, AnimationOut = TextAnimationKind.Fade,
                AnimationInDuration = MediaTime.FromSecondsApprox(0.6), AnimationOutDuration = fade,
            }, Transform.Identity, DefaultDuration),

            TextKind.Credits => (CreditsSample, new TextFormat
            {
                Kind = kind,
                Style = new TextStyle { FontSize = Math.Round(56 * k), LineSpacing = 1.25, Shadow = true, ShadowBlur = Math.Round(4 * k), ShadowOffsetX = 0, ShadowOffsetY = Math.Round(2 * k) },
            }, Transform.Identity, CreditsDuration),

            TextKind.LowerThird => ("Nome Sobrenome\nCargo ou descrição", new TextFormat
            {
                Kind = kind,
                Anchor = TextAnchor.Bottom,
                Style = new TextStyle
                {
                    FontSize = Math.Round(50 * k), Bold = true, Align = TextAlign.Left, MaxWidth = 0.6,
                    BackgroundColor = "#D9101820", AccentColor = "#FF3D9BFF",
                    BackgroundPadding = Math.Round(22 * k), BackgroundRadius = Math.Round(4 * k),
                },
                AnimationIn = TextAnimationKind.SlideRight, AnimationOut = TextAnimationKind.SlideLeft,
            }, Transform.Identity with { X = Math.Round(-W / 2 + W * 0.06), Y = Math.Round(H / 2 - H * 0.1) }, DefaultDuration),

            TextKind.Subtitle => ("Nova legenda", Subtitle(settings), Transform.Identity with { Y = SubtitleY(settings) }, MediaTime.FromSeconds(3)),

            _ => ("Seu texto aqui", new TextFormat
            {
                Kind = TextKind.Text,
                Style = new TextStyle { FontSize = Math.Round(80 * k), Bold = true, Shadow = true, ShadowBlur = Math.Round(6 * k), ShadowOffsetX = Math.Round(3 * k), ShadowOffsetY = Math.Round(3 * k) },
                AnimationIn = TextAnimationKind.Fade, AnimationOut = TextAnimationKind.Fade,
                AnimationInDuration = fade, AnimationOutDuration = fade,
            }, Transform.Identity, DefaultDuration),
        };

        var fr = settings.FrameRate;
        return new Clip
        {
            Id = Guid.NewGuid(),
            Start = fr.SnapToFrame(MediaTime.Max(MediaTime.Zero, at)),
            Duration = Frames.Duration(fr, duration),
            Text = text,
            TextFormat = format,
            Transform = transform,
        };
    }

    /// <summary>Nome padrão de uma nova trilha do tipo (ex.: "Texto 2", "Legendas 1").</summary>
    public static string NewTrackName(Sequence seq, TrackKind kind)
    {
        var prefix = kind == TrackKind.Subtitle ? "Legendas" : "Texto";
        var n = seq.Tracks.Count(t => t.Kind == kind) + 1;
        while (seq.Tracks.Exists(t => t.Name == $"{prefix} {n}")) n++;
        return $"{prefix} {n}";
    }
}

/// <summary>
/// Insere um clipe de texto/legenda na primeira trilha do tipo que esteja livre no intervalo;
/// se nenhuma estiver, cria uma trilha nova no topo (na frente das demais).
/// </summary>
public sealed record AddTextClipCommand(Guid SequenceId, Clip Clip, TrackKind Kind) : IEditCommand
{
    public string Description { get; init; } = Clip.TextFormat?.Kind switch
    {
        TextKind.Title => "Adicionar título",
        TextKind.Credits => "Adicionar créditos",
        TextKind.LowerThird => "Adicionar tarja",
        TextKind.Subtitle => "Adicionar legenda",
        _ => "Adicionar texto",
    };

    /// <summary>Trilha criada quando nenhuma estava livre.</summary>
    public Guid NewTrackId { get; init; } = Guid.NewGuid();

    /// <summary>Trilha preferida (ex.: a selecionada na linha do tempo); usada se for do tipo certo e estiver livre.</summary>
    public Guid? PreferredTrackId { get; init; }

    public Project Apply(Project project)
    {
        if (Kind is not (TrackKind.Text or TrackKind.Subtitle)) throw new EditRejectedException("Textos só entram em trilhas de texto ou de legendas.");
        if (!Clip.IsText) throw new EditRejectedException("O clipe não é um texto.");
        var seq = RequireSequence(project, SequenceId);

        bool Free(Track t) => t.Kind == Kind && !t.IsLocked && !t.Clips.Exists(c => c.Start < Clip.End && c.End > Clip.Start);

        var target = (PreferredTrackId is { } p ? seq.Tracks.Find(t => t.Id == p && Free(t)) : null) ?? seq.Tracks.Find(Free);
        if (target is null)
        {
            target = new Track { Id = NewTrackId, Kind = Kind, Name = TextPresets.NewTrackName(seq, Kind) };
            seq = seq with { Tracks = seq.Tracks.Insert(0, target) };
        }
        return Commit(project, ReplaceTrack(seq, Place(target, Clip)));
    }
}

/// <summary>Uma legenda: intervalo e texto.</summary>
public readonly record struct SubtitleEntry(MediaTime Start, MediaTime End, string Text);

/// <summary>Conversão entre legendas (SRT/VTT) e clipes de legenda (RF140, RF141, RF143).</summary>
public static class SubtitleOps
{
    /// <summary>
    /// Clipes de legenda alinhados a quadros. Legendas sobrepostas são encurtadas para terminar quando a seguinte começa
    /// (uma trilha não sobrepõe clipes); as que ficariam sem duração são descartadas.
    /// </summary>
    public static IReadOnlyList<Clip> ToClips(IEnumerable<SubtitleEntry> entries, ProjectSettings settings, TextFormat? format = null)
    {
        var fr = settings.FrameRate;
        format ??= TextPresets.Subtitle(settings);
        var y = TextPresets.SubtitleY(settings);
        var sorted = entries
            .Select(e => (Start: fr.SnapToFrame(MediaTime.Max(MediaTime.Zero, e.Start)), End: fr.SnapToFrame(e.End), e.Text))
            .Where(e => e.End > e.Start && !string.IsNullOrWhiteSpace(e.Text))
            .OrderBy(e => e.Start)
            .ToList();

        var clips = new List<Clip>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var (start, end, text) = sorted[i];
            if (clips.Count > 0 && start < clips[^1].End) start = clips[^1].End;
            if (i + 1 < sorted.Count && end > sorted[i + 1].Start) end = MediaTime.Max(start, sorted[i + 1].Start);
            if (end <= start) continue;
            clips.Add(new Clip
            {
                Id = Guid.NewGuid(),
                Start = start,
                Duration = end - start,
                Text = text.Replace("\r\n", "\n").Trim(),
                TextFormat = format,
                Transform = Transform.Identity with { Y = y },
            });
        }
        return clips;
    }

    /// <summary>Legendas de uma trilha, na ordem do tempo.</summary>
    public static IReadOnlyList<SubtitleEntry> FromTrack(Track track) =>
        track.Clips.Where(c => c.IsText).OrderBy(c => c.Start).Select(c => new SubtitleEntry(c.Start, c.End, c.Text!)).ToList();

    /// <summary>Legendas de todas as trilhas de legenda visíveis (para exportar em arquivo), ordenadas.</summary>
    public static IReadOnlyList<SubtitleEntry> FromSequence(Sequence sequence) =>
        sequence.Tracks.Where(t => t.Kind == TrackKind.Subtitle && !t.IsHidden)
            .SelectMany(FromTrack).OrderBy(e => e.Start).ToList();
}

/// <summary>
/// Importa legendas (SRT/VTT) como clipes: numa trilha de legendas nova (no topo) ou numa existente,
/// opcionalmente substituindo o conteúdo dela.
/// </summary>
public sealed record ImportSubtitlesCommand(Guid SequenceId, IReadOnlyList<Clip> Clips, Guid? TrackId = null, bool Replace = false) : IEditCommand
{
    public string Description { get; init; } = "Importar legendas";

    public Guid NewTrackId { get; init; } = Guid.NewGuid();

    public string? NewTrackName { get; init; }

    public Project Apply(Project project)
    {
        if (Clips.Count == 0) throw new EditRejectedException("O arquivo não tem legendas para importar.");
        var seq = RequireSequence(project, SequenceId);
        Track track;
        if (TrackId is { } id)
        {
            track = RequireTrack(seq, id);
            if (track.Kind != TrackKind.Subtitle) throw new EditRejectedException("Escolha uma trilha de legendas.");
            EnsureUnlocked(track);
            if (Replace) track = track with { Clips = [], Transitions = [] };
        }
        else
        {
            track = new Track { Id = NewTrackId, Kind = TrackKind.Subtitle, Name = NewTrackName ?? TextPresets.NewTrackName(seq, TrackKind.Subtitle) };
            seq = seq with { Tracks = seq.Tracks.Insert(0, track) };
        }
        foreach (var clip in Clips) track = Place(track, clip);
        return Commit(project, ReplaceTrack(seq, track));
    }
}

/// <summary>Define início e fim de um clipe (editor de legendas, RF143). Não pode invadir clipes vizinhos.</summary>
public sealed record SetClipRangeCommand(Guid SequenceId, Guid ClipId, MediaTime Start, MediaTime End) : IEditCommand
{
    public string Description { get; init; } = "Alterar tempo da legenda";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var (_, track, clip) = Require(seq, ClipId);
        EnsureUnlocked(track);
        var fr = project.Settings.FrameRate;
        var start = fr.SnapToFrame(MediaTime.Max(MediaTime.Zero, Start));
        var end = fr.SnapToFrame(End);
        if (end <= start) throw new EditRejectedException("O fim precisa ser depois do início.");
        if (track.Clips.Exists(c => c.Id != clip.Id && c.Start < end && c.End > start))
            throw new EditRejectedException("O intervalo se sobrepõe a outro clipe da trilha.");
        if (SourceLimit(project, clip) is { } limit && clip.SourceIn + clip.Speed.SourceDurationOf(end - start) > limit)
            throw new EditRejectedException("O clipe não pode ser maior que a mídia.");
        var updated = clip with { Start = start, Duration = end - start };
        return Commit(project, ReplaceTrack(seq, WithClips(track, track.Clips.Replace(clip, updated))));
    }
}
