using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Domain;

public class ProjectValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Project NewProject(ProjectSettings? settings = null) =>
        Project.CreateNew("Projeto", settings ?? new ProjectSettings(), StorageMode.Linked, Now);

    private static Clip NewClip(MediaTime start, MediaTime duration, Guid? mediaId = null) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = mediaId,
        Start = start,
        Duration = duration,
    };

    private static Project WithClips(Project project, params Clip[] clips)
    {
        var seq = project.MainSequence;
        var tracks = seq.Tracks.SetItem(0, seq.Tracks[0] with { Clips = [.. clips] });
        return project.WithSequence(seq with { Tracks = tracks });
    }

    [Fact]
    public void Projeto_novo_valido_nao_tem_erros()
    {
        var errors = ProjectValidator.Validate(NewProject());

        Assert.Empty(errors);
    }

    [Fact]
    public void Projeto_sem_nome_e_reportado()
    {
        var errors = ProjectValidator.Validate(NewProject() with { Name = " " });

        Assert.Contains(errors, e => e.Contains("nome"));
    }

    [Fact]
    public void Identificadores_duplicados_sao_reportados()
    {
        var project = NewProject();
        var seq = project.MainSequence;
        var original = seq.Tracks[0];
        var duplicated = project.WithSequence(seq with
        {
            Tracks = seq.Tracks.Add(original with { Name = "Copia" }), // mesmo Id da primeira trilha
        });

        var errors = ProjectValidator.Validate(duplicated);

        Assert.Contains(errors, e => e.Contains("Identificador duplicado em trilha"));
    }

    [Fact]
    public void Clipes_sobrepostos_na_mesma_trilha_sao_reportados()
    {
        var project = WithClips(
            NewProject(),
            NewClip(MediaTime.Zero, MediaTime.FromSeconds(2)),
            NewClip(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2)));

        var errors = ProjectValidator.Validate(project);

        Assert.Contains(errors, e => e.Contains("sobrepostos"));
    }

    [Fact]
    public void Clipes_encostados_nao_sao_sobrepostos()
    {
        var project = WithClips(
            NewProject(),
            NewClip(MediaTime.Zero, MediaTime.FromSeconds(2)),
            NewClip(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2)));

        Assert.Empty(ProjectValidator.Validate(project));
    }

    [Fact]
    public void Clipe_que_referencia_midia_inexistente_e_reportado()
    {
        var project = WithClips(NewProject(), NewClip(MediaTime.Zero, MediaTime.FromSeconds(1), Guid.NewGuid()));

        var errors = ProjectValidator.Validate(project);

        Assert.Contains(errors, e => e.Contains("mídia inexistente"));
    }

    [Fact]
    public void Configuracoes_invalidas_sao_reportadas()
    {
        var project = NewProject(new ProjectSettings { Width = 1921 });

        var errors = ProjectValidator.Validate(project);

        Assert.Contains(errors, e => e.Contains("pares"));
    }
}
