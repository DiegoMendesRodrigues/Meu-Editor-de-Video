// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Reflection;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.DesktopTests.Support;

/// <summary>
/// Implementação "vazia" de qualquer interface (via <see cref="DispatchProxy"/>): cada membro devolve o valor padrão
/// (tarefas concluídas, listas vazias, null) a não ser que o teste registre um comportamento com <see cref="On"/>.
/// Evita escrever uma classe falsa inteira por interface só para abrir um diálogo.
/// </summary>
public class Stub : DispatchProxy
{
    private readonly Dictionary<string, Func<object?[], object?>> _handlers = [];

    /// <summary>Cria o falso de <typeparamref name="T"/>.</summary>
    public static T Of<T>(Action<StubSetup<T>>? configure = null) where T : class
    {
        var proxy = Create<T, Stub>();
        configure?.Invoke(new StubSetup<T>((Stub)(object)proxy));
        return proxy;
    }

    internal void Register(string member, Func<object?[], object?> handler) => _handlers[member] = handler;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (_handlers.TryGetValue(targetMethod.Name, out var handler)) return handler(args ?? []);
        return DefaultFor(targetMethod.ReturnType);
    }

    private static object? DefaultFor(Type type)
    {
        if (type == typeof(void)) return null;
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = type.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [DefaultFor(inner)]);
        }
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyList<>) || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyCollection<>))
                return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        }
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

/// <summary>Registro de comportamentos de um <see cref="Stub"/>.</summary>
public sealed class StubSetup<T>(Stub stub) where T : class
{
    /// <summary>Define o que o método (ou o getter <c>get_Nome</c>) devolve.</summary>
    public StubSetup<T> On(string member, Func<object?[], object?> handler)
    {
        stub.Register(member, handler);
        return this;
    }

    /// <summary>Define o valor de uma propriedade.</summary>
    public StubSetup<T> Returns(string property, object? value) => On("get_" + property, _ => value);
}

/// <summary>Montagem dos serviços falsos usados pelos testes dos diálogos.</summary>
public static class DialogsFakes
{
    /// <summary>Configurações em memória (nada é gravado em disco).</summary>
    public sealed class MemorySettings(AppSettings? initial = null) : ISettingsService
    {
        public AppSettings Current { get; private set; } = initial ?? new AppSettings();
        public int Saves { get; private set; }

        public event EventHandler<AppSettings>? Changed;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Saves++;
            Changed?.Invoke(this, settings);
            return Task.CompletedTask;
        }
    }

    /// <summary>Serviço de projetos real sobre armazenamento falso: só para ter uma sessão com o projeto dado.</summary>
    public static IProjectService ProjectsWith(Project project, MediaResolution? media = null)
    {
        var store = Stub.Of<IProjectStore>(s => s.On(nameof(IProjectStore.LoadAsync),
            _ => Task.FromResult(new LoadedProject(project, "C:\\Projetos\\Meu filme.med", "1", false, []))));
        var locator = Stub.Of<IMediaLocator>(s => s.On(nameof(IMediaLocator.ResolveAsync),
            _ => Task.FromResult(media ?? MediaResolution.Empty)));
        var autosave = Stub.Of<IAutosaveStore>();
        var service = new ProjectService(store, locator, autosave, new MemorySettings(), NullLogger<ProjectService>.Instance);
        service.OpenAsync("C:\\Projetos\\Meu filme.med").GetAwaiter().GetResult();
        return service;
    }

    /// <summary>Projeto de exemplo com uma mídia de vídeo.</summary>
    public static (Project Project, MediaAsset Asset) SampleProject(string name = "Férias na praia")
    {
        var project = Project.CreateNew(name, new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now);
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Kind = MediaKind.Video,
            Name = "Praia.mp4",
            OriginalPath = "D:\\Vídeos\\Praia.mp4",
            Duration = MediaTime.FromSecondsApprox(42),
            Width = 1920,
            Height = 1080,
        };
        project = new AddMediaAssetsCommand([asset]).Apply(project);
        return (project, asset);
    }
}
