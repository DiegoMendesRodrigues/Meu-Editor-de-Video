using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>
/// Operação de edição. Recebe o estado atual e devolve o novo estado (o original nunca é alterado).
/// Lança <see cref="EditRejectedException"/> quando a operação não é permitida.
/// </summary>
public interface IEditCommand
{
    /// <summary>Descrição exibida no histórico (pt-BR), ex.: "Renomear projeto".</summary>
    string Description { get; }

    /// <summary>
    /// Chave para mesclar comandos consecutivos do mesmo tipo (ex.: arrastar um controle deslizante).
    /// Nulo = nunca mesclar.
    /// </summary>
    string? MergeKey => null;

    Project Apply(Project project);
}

/// <summary>Operação recusada por regra de negócio; a mensagem é exibida ao usuário.</summary>
public sealed class EditRejectedException(string message) : Exception(message);

public sealed record HistoryEntry(string Description, DateTimeOffset At);

/// <summary>
/// Histórico de alterações com desfazer/refazer (RF012–RF014).
/// Guarda estados imutáveis completos; como o modelo compartilha estrutura, o custo de memória é pequeno.
/// Não é thread-safe: use a partir da thread da interface.
/// </summary>
public sealed class EditHistory
{
    public const int DefaultCapacity = 500;
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(1.5);

    private readonly List<Project> _states = [];
    private readonly List<(HistoryEntry Entry, string? MergeKey)> _entries = [];
    private readonly int _capacity;
    private readonly TimeProvider _time;
    private int _position;   // índice em _states do estado atual
    private int _savedPosition; // -1 quando o estado salvo saiu do histórico

    public EditHistory(Project initial, int capacity = DefaultCapacity, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _time = time ?? TimeProvider.System;
        _states.Add(initial);
    }

    public event EventHandler? Changed;

    public Project Current => _states[_position];

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _states.Count - 1;

    public string? UndoDescription => CanUndo ? _entries[_position - 1].Entry.Description : null;
    public string? RedoDescription => CanRedo ? _entries[_position].Entry.Description : null;

    /// <summary>Entradas do histórico, da mais antiga para a mais recente (inclui as que podem ser refeitas).</summary>
    public IReadOnlyList<HistoryEntry> Entries => _entries.Select(e => e.Entry).ToList();

    /// <summary>Quantidade de entradas aplicadas (as demais podem ser refeitas).</summary>
    public int AppliedCount => _position;

    /// <summary>Há alterações desde o último <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => _position != _savedPosition;

    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var next = command.Apply(Current);
        if (ReferenceEquals(next, Current)) return; // nada mudou

        var now = _time.GetUtcNow();

        // Descarta o que poderia ser refeito.
        if (CanRedo)
        {
            _states.RemoveRange(_position + 1, _states.Count - _position - 1);
            _entries.RemoveRange(_position, _entries.Count - _position);
            if (_savedPosition > _position) _savedPosition = -1;
        }

        var last = _entries.Count > 0 ? _entries[^1] : default;
        var canMerge = command.MergeKey is not null && _entries.Count > 0 && _position != _savedPosition
                       && last.MergeKey == command.MergeKey && now - last.Entry.At <= MergeWindow;

        if (canMerge)
        {
            _states[_position] = next;
            _entries[^1] = (last.Entry with { At = now }, last.MergeKey);
        }
        else
        {
            _states.Add(next);
            _entries.Add((new HistoryEntry(command.Description, now), command.MergeKey));
            _position++;
            TrimToCapacity();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _position--;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _position++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Volta/avança até um ponto do histórico (0 = estado inicial; n = após a n-ésima entrada).</summary>
    public void GoTo(int appliedCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(appliedCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(appliedCount, _entries.Count);
        if (appliedCount == _position) return;
        _position = appliedCount;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved()
    {
        _savedPosition = _position;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Substitui o estado atual sem criar entrada de desfazer (ex.: atualizar ModifiedAt ao salvar).
    /// </summary>
    public void ReplaceCurrentSilently(Project project) => _states[_position] = project;

    private void TrimToCapacity()
    {
        var excess = _entries.Count - _capacity;
        if (excess <= 0) return;
        _entries.RemoveRange(0, excess);
        _states.RemoveRange(0, excess);
        _position -= excess;
        _savedPosition = _savedPosition >= excess ? _savedPosition - excess : -1;
    }
}
