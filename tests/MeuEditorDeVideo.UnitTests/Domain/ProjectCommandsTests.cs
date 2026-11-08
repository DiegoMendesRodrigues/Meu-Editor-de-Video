using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Domain;

public class ProjectCommandsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Project NewProject() =>
        Project.CreateNew("Projeto", new ProjectSettings(), StorageMode.Linked, Now);

    private static MediaAsset NewAsset(string name = "clip.mp4") => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = name,
        OriginalPath = @"C:\Midia\" + name,
    };

    private static Clip NewClip(Guid? mediaId, MediaTime start, MediaTime duration) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = mediaId,
        Start = start,
        Duration = duration,
    };

    /// <summary>Adiciona um clipe na primeira trilha da sequência principal.</summary>
    private static Project WithClip(Project project, Clip clip)
    {
        var seq = project.MainSequence;
        var tracks = seq.Tracks.SetItem(0, seq.Tracks[0] with { Clips = seq.Tracks[0].Clips.Add(clip) });
        return project.WithSequence(seq with { Tracks = tracks });
    }

    [Fact]
    public void RenameProjectCommand_remove_espacos_e_altera_o_nome()
    {
        var result = new RenameProjectCommand("  Novo nome  ").Apply(NewProject());

        Assert.Equal("Novo nome", result.Name);
        Assert.Equal("Renomear projeto", new RenameProjectCommand("x").Description);
    }

    [Fact]
    public void RenameProjectCommand_recusa_nome_vazio_ou_com_caractere_invalido()
    {
        Assert.Throws<EditRejectedException>(() => new RenameProjectCommand("   ").Apply(NewProject()));
        Assert.Throws<EditRejectedException>(() => new RenameProjectCommand("a/b").Apply(NewProject()));
    }

    [Fact]
    public void RemoveMediaAssetCommand_e_recusada_quando_um_clipe_usa_a_midia()
    {
        var asset = NewAsset();
        var project = NewProject() with { Media = [asset] };
        var used = WithClip(project, NewClip(asset.Id, MediaTime.Zero, MediaTime.FromSeconds(2)));

        var ex = Assert.Throws<EditRejectedException>(() => new RemoveMediaAssetCommand(asset.Id).Apply(used));

        Assert.Contains("em uso", ex.Message);
        Assert.Single(used.Media);
    }

    [Fact]
    public void RemoveMediaAssetCommand_remove_a_midia_quando_nao_ha_clipes()
    {
        var asset = NewAsset();
        var project = NewProject() with { Media = [asset] };

        var result = new RemoveMediaAssetCommand(asset.Id).Apply(project);

        Assert.Empty(result.Media);
    }

    [Fact]
    public void RemoveTrackCommand_e_recusada_quando_a_trilha_esta_bloqueada()
    {
        var project = NewProject();
        var seq = project.MainSequence;
        var track = seq.Tracks[0];

        var locked = UpdateTrackCommand.SetLocked(seq.Id, track.Id, true).Apply(project);

        var ex = Assert.Throws<EditRejectedException>(() => new RemoveTrackCommand(seq.Id, track.Id).Apply(locked));
        Assert.Contains("bloqueada", ex.Message);
        Assert.Equal(4, locked.MainSequence.Tracks.Count);
    }

    [Fact]
    public void RemoveTrackCommand_remove_trilha_desbloqueada()
    {
        var project = NewProject();
        var seq = project.MainSequence;
        var track = seq.Tracks[0];

        var result = new RemoveTrackCommand(seq.Id, track.Id).Apply(project);

        Assert.Equal(3, result.MainSequence.Tracks.Count);
        Assert.DoesNotContain(result.MainSequence.Tracks, t => t.Id == track.Id);
    }

    [Fact]
    public void ChangeProjectSettingsCommand_ao_mudar_o_fps_alinha_clipes_a_nova_grade()
    {
        var clip = NewClip(null, FrameRate.Fps30.FrameToTime(7), MediaTime.FromSeconds(1));
        var project = WithClip(NewProject(), clip);
        var settings = project.Settings with { FrameRate = FrameRate.Fps25 };

        var result = new ChangeProjectSettingsCommand(settings).Apply(project);

        var moved = Assert.Single(result.MainSequence.Tracks[0].Clips);
        Assert.Equal(FrameRate.Fps25.FrameToTime(6), moved.Start);
        Assert.Equal(settings, result.Settings);
    }

    [Fact]
    public void ChangeProjectSettingsCommand_recusa_configuracao_invalida()
    {
        var invalid = NewProject().Settings with { Width = 1921 };

        Assert.Throws<EditRejectedException>(() => new ChangeProjectSettingsCommand(invalid).Apply(NewProject()));
    }

    [Fact]
    public void AddMarkerCommand_alinha_o_tempo_ao_quadro_mais_proximo()
    {
        var project = NewProject();
        var seq = project.MainSequence;

        var result = new AddMarkerCommand(seq.Id, MediaTime.FromSecondsApprox(1.02), "M").Apply(project);

        var marker = Assert.Single(result.MainSequence.Markers);
        Assert.Equal(FrameRate.Fps30.FrameToTime(31), marker.Time);
        Assert.Equal("M", marker.Name);
    }

    [Fact]
    public void AddMarkerCommand_limita_tempo_negativo_a_zero_e_ordena_os_marcadores()
    {
        var project = NewProject();
        var seqId = project.MainSequence.Id;

        var withNegative = new AddMarkerCommand(seqId, new MediaTime(-500)).Apply(project);
        var withLate = new AddMarkerCommand(seqId, MediaTime.FromSeconds(5)).Apply(withNegative);
        var withMiddle = new AddMarkerCommand(seqId, MediaTime.FromSeconds(3)).Apply(withLate);
        var withSecond = new AddMarkerCommand(seqId, MediaTime.FromSeconds(2)).Apply(withMiddle);

        var markers = withSecond.MainSequence.Markers;
        Assert.Equal(4, markers.Count);
        Assert.Equal(MediaTime.Zero, markers[0].Time);
        Assert.Equal(MediaTime.FromSeconds(2), markers[1].Time);
        Assert.Equal(MediaTime.FromSeconds(3), markers[2].Time);
        Assert.Equal(MediaTime.FromSeconds(5), markers[3].Time);
    }

    [Fact]
    public void CompositeCommand_aplica_todos_os_comandos_na_ordem()
    {
        var composite = new CompositeCommand("Dois passos", [new RenameProjectCommand("Um"), new RenameProjectCommand("Dois")]);

        var result = composite.Apply(NewProject());

        Assert.Equal("Dois", result.Name);
    }

    [Fact]
    public void CompositeCommand_vira_uma_unica_entrada_no_historico()
    {
        var project = NewProject();
        var history = new EditHistory(project);
        var seqId = project.MainSequence.Id;
        var composite = new CompositeCommand("Montagem",
        [
            new RenameProjectCommand("Um"),
            new AddTrackCommand(seqId, TrackKind.Audio, "Música 2"),
        ]);

        history.Execute(composite);

        Assert.Equal(1, history.AppliedCount);
        Assert.Equal("Montagem", history.UndoDescription);
        Assert.Equal(5, history.Current.MainSequence.Tracks.Count);

        history.Undo();

        Assert.False(history.CanUndo);
        Assert.Equal("Projeto", history.Current.Name);
        Assert.Equal(4, history.Current.MainSequence.Tracks.Count);
    }
}
