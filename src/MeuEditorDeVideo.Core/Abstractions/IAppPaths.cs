namespace MeuEditorDeVideo.Core.Abstractions;

/// <summary>Diretórios usados pelo aplicativo. Todos ficam no computador do usuário (RNF003).</summary>
public interface IAppPaths
{
    /// <summary>
    /// Configurações e modelos de exportação do usuário: %APPDATA%\MeuEditorDeVideo no Windows,
    /// ~/.config/MeuEditorDeVideo no Linux e ~/Library/Application Support/MeuEditorDeVideo no macOS.
    /// </summary>
    string RoamingData { get; }

    /// <summary>
    /// Cache, logs, autosave e modelos de IA: %LOCALAPPDATA%\MeuEditorDeVideo no Windows,
    /// ~/.local/share/MeuEditorDeVideo no Linux e ~/Library/Application Support/MeuEditorDeVideo no macOS.
    /// </summary>
    string LocalData { get; }

    string Logs { get; }
    string Cache { get; }
    string Autosave { get; }
    string AiModels { get; }
    string Backups { get; }
    string SettingsFile { get; }
}
