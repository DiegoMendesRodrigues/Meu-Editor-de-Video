using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Application.Projects;

/// <summary>Projeto aberto: estado editável, arquivo de origem e localização das mídias.</summary>
public sealed class ProjectSession
{
    private volatile Project _snapshot;

    internal ProjectSession(Project project, string? filePath, MediaResolution media, bool recovered, bool migrated)
    {
        History = new EditHistory(project);
        _snapshot = project;
        FilePath = filePath;
        Media = media;
        IsRecovered = recovered;
        WasMigrated = migrated;
        History.Changed += (_, _) => _snapshot = History.Current;
    }

    public EditHistory History { get; }

    /// <summary>Estado atual (use na thread da interface).</summary>
    public Project Project => History.Current;

    /// <summary>Último estado publicado, seguro para leitura em segundo plano (autosave).</summary>
    public Project Snapshot => _snapshot;

    public string? FilePath { get; internal set; }

    public MediaResolution Media { get; internal set; }

    /// <summary>Aberto a partir de um salvamento automático; permanece "alterado" até ser salvo.</summary>
    public bool IsRecovered { get; internal set; }

    /// <summary>Arquivo convertido de um formato antigo; o original recebe cópia de segurança antes de ser sobrescrito.</summary>
    public bool WasMigrated { get; internal set; }

    public bool IsDirty => History.IsDirty || IsRecovered;

    public bool HasMissingMedia => Media.Missing.Any();

    internal void Publish() => _snapshot = History.Current;
}
