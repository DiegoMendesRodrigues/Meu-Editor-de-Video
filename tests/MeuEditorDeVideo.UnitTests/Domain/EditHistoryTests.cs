using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.UnitTests.Domain;

public class EditHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Project NewProject(string name = "Inicial") =>
        Project.CreateNew(name, new ProjectSettings(), StorageMode.Linked, Start);

    private static TestCommand Rename(string name, string? mergeKey = null) =>
        new($"Renomear para {name}", p => p with { Name = name }, mergeKey);

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestCommand(string description, Func<Project, Project> apply, string? mergeKey = null) : IEditCommand
    {
        public string Description { get; } = description;

        public string? MergeKey { get; } = mergeKey;

        public Project Apply(Project project) => apply(project);
    }

    [Fact]
    public void Execute_undo_e_redo_alteram_o_estado_atual()
    {
        var history = new EditHistory(NewProject());

        history.Execute(Rename("A"));
        Assert.Equal("A", history.Current.Name);

        history.Undo();
        Assert.Equal("Inicial", history.Current.Name);

        history.Redo();
        Assert.Equal("A", history.Current.Name);
    }

    [Fact]
    public void Novo_comando_apos_desfazer_descarta_o_refazer()
    {
        var history = new EditHistory(NewProject());
        history.Execute(Rename("A"));
        history.Execute(Rename("B"));
        history.Undo();

        history.Execute(Rename("C"));

        Assert.False(history.CanRedo);
        Assert.Null(history.RedoDescription);
        Assert.Equal("C", history.Current.Name);
        Assert.Equal(2, history.AppliedCount);

        history.Undo();
        Assert.Equal("A", history.Current.Name);
    }

    [Fact]
    public void CanUndo_CanRedo_e_descricoes_acompanham_a_posicao()
    {
        var history = new EditHistory(NewProject());
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.UndoDescription);
        Assert.Null(history.RedoDescription);

        history.Execute(Rename("A"));
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("Renomear para A", history.UndoDescription);
        Assert.Null(history.RedoDescription);

        history.Undo();
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Null(history.UndoDescription);
        Assert.Equal("Renomear para A", history.RedoDescription);
    }

    [Fact]
    public void IsDirty_fica_falso_apos_salvar_e_volta_a_verdadeiro_ao_desfazer_alem_do_ponto_salvo()
    {
        var history = new EditHistory(NewProject());
        Assert.False(history.IsDirty);

        history.Execute(Rename("A"));
        Assert.True(history.IsDirty);

        history.MarkSaved();
        Assert.False(history.IsDirty);

        history.Undo();
        Assert.True(history.IsDirty);

        history.Redo();
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void Comandos_com_mesma_MergeKey_dentro_da_janela_viram_uma_unica_entrada()
    {
        var clock = new FakeTime();
        var history = new EditHistory(NewProject(), time: clock);

        history.Execute(Rename("A", "zoom"));
        clock.Now = Start.AddSeconds(0.5);
        history.Execute(Rename("B", "zoom"));

        Assert.Equal(1, history.AppliedCount);
        Assert.Equal("B", history.Current.Name);

        history.Undo();
        Assert.Equal("Inicial", history.Current.Name);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Comandos_fora_da_janela_ou_com_chave_diferente_nao_mesclam()
    {
        var clock = new FakeTime();
        var history = new EditHistory(NewProject(), time: clock);

        history.Execute(Rename("A", "zoom"));
        clock.Now = Start.AddSeconds(2);
        history.Execute(Rename("B", "zoom"));
        Assert.Equal(2, history.AppliedCount);

        clock.Now = Start.AddSeconds(2.5);
        history.Execute(Rename("C", "volume"));
        Assert.Equal(3, history.AppliedCount);
    }

    [Fact]
    public void Comando_recusado_nao_altera_estado_nem_dispara_evento()
    {
        var history = new EditHistory(NewProject());
        history.Execute(Rename("A"));
        history.MarkSaved();
        history.Undo();

        var events = 0;
        history.Changed += (_, _) => events++;

        Assert.Throws<EditRejectedException>(() => history.Execute(new RenameProjectCommand("   ")));

        Assert.Equal("Inicial", history.Current.Name);
        Assert.Equal(0, history.AppliedCount);
        Assert.True(history.CanRedo);
        Assert.True(history.IsDirty);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Comando_sem_efeito_nao_cria_entrada()
    {
        var history = new EditHistory(NewProject("Igual"));

        history.Execute(new RenameProjectCommand("Igual"));

        Assert.Equal(0, history.AppliedCount);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Capacidade_limita_entradas_e_mantem_as_mais_recentes()
    {
        var history = new EditHistory(NewProject(), capacity: 3);

        for (var i = 1; i <= 5; i++) history.Execute(Rename($"N{i}"));

        Assert.Equal(3, history.AppliedCount);
        Assert.Equal(3, history.Entries.Count);
        Assert.Equal("N5", history.Current.Name);

        history.Undo();
        history.Undo();
        history.Undo();
        Assert.False(history.CanUndo);
        Assert.Equal("N2", history.Current.Name);
    }

    [Fact]
    public void Capacidade_padrao_e_500_entradas()
    {
        var history = new EditHistory(NewProject());

        for (var i = 1; i <= EditHistory.DefaultCapacity + 1; i++) history.Execute(Rename($"N{i}"));

        Assert.Equal(500, EditHistory.DefaultCapacity);
        Assert.Equal(EditHistory.DefaultCapacity, history.AppliedCount);
    }

    [Fact]
    public void GoTo_salta_para_qualquer_ponto_do_historico()
    {
        var history = new EditHistory(NewProject());
        history.Execute(Rename("A"));
        history.Execute(Rename("B"));
        history.Execute(Rename("C"));

        history.GoTo(1);
        Assert.Equal("A", history.Current.Name);

        history.GoTo(0);
        Assert.Equal("Inicial", history.Current.Name);

        history.GoTo(3);
        Assert.Equal("C", history.Current.Name);

        Assert.Throws<ArgumentOutOfRangeException>(() => history.GoTo(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.GoTo(-1));
    }

    [Fact]
    public void Changed_dispara_em_execute_undo_redo_goto_e_markSaved()
    {
        var history = new EditHistory(NewProject());
        var count = 0;
        history.Changed += (_, _) => count++;

        history.Execute(Rename("A"));
        Assert.Equal(1, count);

        history.Undo();
        Assert.Equal(2, count);

        history.Redo();
        Assert.Equal(3, count);

        history.GoTo(0);
        Assert.Equal(4, count);

        history.MarkSaved();
        Assert.Equal(5, count);

        history.Redo();
        Assert.Equal(6, count);

        history.Undo();
        Assert.Equal(7, count);

        history.Undo(); // ja no inicio: nada muda, sem evento
        Assert.Equal(7, count);
    }
}
