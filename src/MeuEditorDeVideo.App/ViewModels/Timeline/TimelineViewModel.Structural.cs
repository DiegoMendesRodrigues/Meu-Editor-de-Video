using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.App.ViewModels.Timeline;

/// <summary>Recursos estruturais: camada de ajuste, clipe composto, ativar/desativar, tela dividida e multicâmera.</summary>
public sealed partial class TimelineViewModel
{
    /// <summary>Modelos de tela dividida (menu Projeto › Tela dividida).</summary>
    public IReadOnlyList<SplitScreenLayout> SplitScreenLayouts { get; } = Domain.Editing.SplitScreenLayouts.All;

    private IEnumerable<Clip> SelectedClips() =>
        Sequence is { } seq ? seq.Tracks.SelectMany(t => t.Clips).Where(c => _selection.Contains(c.Id)) : [];

    /// <summary>Camada de ajuste de 5 s no cursor (efeitos dela valem para tudo o que está abaixo).</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private void AddAdjustmentLayer()
    {
        var command = new AddAdjustmentLayerCommand(SequenceId, Playhead, MediaTime.FromSeconds(5));
        if (!Execute(command)) return;
        Select(command.ClipId, additive: false);
        RevealTopRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Uma trilha nova entrou no topo (a view rola a linha do tempo até ela).</summary>
    public event EventHandler? RevealTopRequested;

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void CreateCompound()
    {
        var name = _dialogs.Prompt("Criar clipe composto", "Nome do clipe composto", "Clipe composto");
        if (name is null) return;
        var command = new CreateCompoundClipCommand(SequenceId, [.. _selection], string.IsNullOrWhiteSpace(name) ? "Clipe composto" : name.Trim());
        if (Execute(command)) Select(command.NewClipId, additive: false);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void UnpackCompound()
    {
        var clip = SelectedClips().FirstOrDefault(c => c.IsCompound);
        if (clip is null)
        {
            _dialogs.ShowInfo("Desfazer clipe composto", "Selecione um clipe composto.");
            return;
        }
        Execute(new UnpackCompoundClipCommand(SequenceId, clip.Id));
    }

    /// <summary>Desativa os clipes selecionados (ou ativa, se o primeiro já estiver desativado).</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void ToggleEnabled()
    {
        var clips = SelectedClips().ToList();
        if (clips.Count == 0) return;
        Execute(new SetClipsEnabledCommand(SequenceId, [.. clips.Select(c => c.Id)], !clips[0].Enabled));
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void SplitScreen(string? layoutId)
    {
        if (layoutId is null) return;
        Execute(new SplitScreenCommand(SequenceId, [.. _selection], layoutId));
    }

    /// <summary>Multicâmera: mostra a câmera N (trilha de vídeo N de cima para baixo no cursor) a partir do cursor.</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private void SwitchCamera(string? angle)
    {
        if (!int.TryParse(angle, out var n) || n < 1) return;
        Execute(new MulticamSwitchCommand(SequenceId, n - 1, Playhead));
    }
}
