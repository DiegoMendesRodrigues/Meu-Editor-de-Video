// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Styling;

namespace MeuEditorDeVideo.Desktop.Views.Panels;

/// <summary>
/// Associa os itens do inspetor (parâmetros de efeito, valores animáveis, efeitos da pilha) aos modelos de
/// <c>InspectorTemplates.axaml</c>. No WPF eram <c>DataTemplate DataType</c> implícitos num dicionário de recursos; no
/// Avalonia só os modelos de controles e do aplicativo são implícitos, então este seletor (registrado em
/// <c>App.axaml</c>) repassa cada item ao modelo cujo <c>DataType</c> combina.
/// </summary>
public sealed class InspectorTemplateSelector : IDataTemplate
{
    private static readonly Lazy<IReadOnlyList<IDataTemplate>> Templates = new(Load);

    private static IReadOnlyList<IDataTemplate> Load()
    {
        var include = new ResourceInclude((Uri?)null) { Source = new Uri("avares://MeuEditorDeVideo/Views/Panels/InspectorTemplates.axaml") };
        // Os recursos do dicionário são carregados sob demanda: TryGetResource materializa cada modelo.
        var dictionary = include.Loaded;
        var templates = new List<IDataTemplate>();
        foreach (var key in dictionary.Keys)
            if (dictionary.TryGetResource(key, null, out var value) && value is IDataTemplate template) templates.Add(template);
        return templates;
    }

    public bool Match(object? data) => data is not null && Templates.Value.Any(t => t.Match(data));

    public Control? Build(object? data) => Templates.Value.FirstOrDefault(t => t.Match(data))?.Build(data);
}
