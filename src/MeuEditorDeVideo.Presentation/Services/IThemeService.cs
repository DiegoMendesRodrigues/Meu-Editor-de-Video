using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.Presentation.Services;

/// <summary>Tema claro/escuro da interface (RF207); a implementação fica no aplicativo.</summary>
public interface IThemeService
{
    AppTheme Current { get; }

    void Apply(AppTheme theme);
}
