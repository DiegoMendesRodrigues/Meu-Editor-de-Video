namespace MeuEditorDeVideo.Core.Abstractions;

/// <summary>Diretórios usados pelo aplicativo. Todos ficam no computador do usuário (RNF003).</summary>
public interface IAppPaths
{
    /// <summary>%APPDATA%\MeuEditorDeVideo — configurações e modelos de exportação do usuário.</summary>
    string RoamingData { get; }

    /// <summary>%LOCALAPPDATA%\MeuEditorDeVideo — cache, logs, autosave, modelos de IA.</summary>
    string LocalData { get; }

    string Logs { get; }
    string Cache { get; }
    string Autosave { get; }
    string AiModels { get; }
    string SettingsFile { get; }
}
