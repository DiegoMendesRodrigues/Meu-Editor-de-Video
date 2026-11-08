using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Agrupa vários comandos numa única entrada do histórico (desfeitos juntos).</summary>
public sealed record CompositeCommand(string Description, IReadOnlyList<IEditCommand> Commands) : IEditCommand
{
    public Project Apply(Project project) => Commands.Aggregate(project, (p, c) => c.Apply(p));
}
