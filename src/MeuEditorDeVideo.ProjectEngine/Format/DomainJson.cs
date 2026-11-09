using System.Text.Json;
using MeuEditorDeVideo.Core.Projects;

namespace MeuEditorDeVideo.ProjectEngine.Format;

/// <summary>Implementação de <see cref="IDomainJson"/> com as opções do formato .med.</summary>
public sealed class DomainJson : IDomainJson
{
    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, MedJson.Options);

    public T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, MedJson.Options);
}
