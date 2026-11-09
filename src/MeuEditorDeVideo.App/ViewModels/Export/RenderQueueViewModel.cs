using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Core.Export;

namespace MeuEditorDeVideo.App.ViewModels.Export;

/// <summary>Linha da fila de renderização: estado, progresso, velocidade e tempo restante.</summary>
public sealed partial class RenderJobViewModel(RenderJob job) : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    [ObservableProperty] public partial RenderJob Job { get; set; } = job;
    [ObservableProperty] public partial double Percent { get; set; }
    [ObservableProperty] public partial string Detail { get; set; } = "";

    public Guid Id => Job.Id;
    public string Name => Job.Name;
    public string Template => Job.TemplateName;
    public string Format => $"{ExportFormats.ContainerName(Job.Settings.Container)} · {ExportFormats.VideoCodecName(Job.Settings.VideoCodec)}";
    public bool IsRunning => Job.Status == RenderJobStatus.Running;
    public bool CanCancel => Job.IsActive;
    public bool CanResume => Job.CanResume;
    public bool CanRemove => !Job.IsActive;
    public bool CanOpen => Job.Status == RenderJobStatus.Completed && Job.Files.Count > 0;
    public bool HasError => Job.Error is not null;

    public string StatusText => Job.Status switch
    {
        RenderJobStatus.Queued => "Na fila",
        RenderJobStatus.Running => "Renderizando",
        RenderJobStatus.Interrupted => "Interrompida — pode ser retomada",
        RenderJobStatus.Completed => Job.Files.Count > 1 ? $"Concluída ({Job.Files.Count} arquivos)" : "Concluída",
        RenderJobStatus.Failed => "Falhou",
        RenderJobStatus.Canceled => "Cancelada",
        _ => "",
    };

    partial void OnJobChanged(RenderJob value)
    {
        OnPropertyChanged(string.Empty);
        if (value.Status == RenderJobStatus.Completed)
        {
            Percent = 100;
            Detail = string.Join("  ", new[]
            {
                value.Elapsed is { } e ? $"Tempo: {Duration(e)}" : null,
                value.Encoder is { } enc ? $"Codificador: {enc}" : null,
            }.Where(x => x is not null)) + (value.Warnings.Count > 0 ? "\n" + string.Join("\n", value.Warnings) : "");
        }
        else if (value.Status is RenderJobStatus.Failed)
        {
            Detail = value.Error ?? "";
        }
        else if (value.Status is RenderJobStatus.Queued or RenderJobStatus.Canceled or RenderJobStatus.Interrupted)
        {
            Detail = "";
        }
    }

    public void Apply(RenderProgress p)
    {
        Percent = Math.Round(p.Fraction * 100, 1);
        var stage = p.Stage switch
        {
            RenderStage.Preparing => "Preparando",
            RenderStage.Encoding => string.Create(PtBr, $"Quadro {p.FramesDone:N0} de {p.TotalFrames:N0}"),
            RenderStage.Muxing => "Gravando o arquivo",
            _ => "Verificando o resultado",
        };
        var parts = p.Parts > 1 ? $" · arquivo {p.Part} de {p.Parts}" : "";
        var speed = p.FramesPerSecond > 0 ? string.Create(PtBr, $" · {p.FramesPerSecond:0.#} quadros/s") : "";
        var eta = p.Remaining is { } r ? $" · faltam {Duration(r)}" : "";
        Detail = string.Create(PtBr, $"{Percent:0.#}% · {stage}{parts}{speed} · decorrido {Duration(p.Elapsed)}{eta}");
    }

    private static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", PtBr) : t.ToString(@"m\:ss", PtBr);
}

/// <summary>Painel da fila de renderização em segundo plano.</summary>
public sealed partial class RenderQueueViewModel : ObservableObject, IDisposable
{
    private readonly IRenderQueueService _queue;
    private readonly Dispatcher _dispatcher;

    public RenderQueueViewModel(IRenderQueueService queue)
    {
        _queue = queue;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _queue.Changed += OnChanged;
        _queue.ProgressChanged += OnProgress;
        Refresh();
    }

    public ObservableCollection<RenderJobViewModel> Jobs { get; } = [];

    public bool IsEmpty => Jobs.Count == 0;

    /// <summary>Usado no modo "não renderizar em segundo plano": a janela fecha quando este trabalho termina.</summary>
    public Guid? FollowJob
    {
        get => _followJob;
        set
        {
            _followJob = value;
            // O trabalho pode terminar antes de a janela começar a acompanhar (ex.: falha imediata).
            _dispatcher.BeginInvoke(DispatcherPriority.Background, Refresh);
        }
    }

    private Guid? _followJob;

    public event EventHandler<RenderJob>? FollowedJobFinished;

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(Refresh);

    private void OnProgress(object? sender, RenderJobProgress e) => _dispatcher.BeginInvoke(() =>
    {
        Jobs.FirstOrDefault(j => j.Id == e.JobId)?.Apply(e.Progress);
    });

    private void Refresh()
    {
        var jobs = _queue.Jobs;
        for (var i = Jobs.Count - 1; i >= 0; i--)
            if (jobs.All(j => j.Id != Jobs[i].Id)) Jobs.RemoveAt(i);
        foreach (var job in jobs)
        {
            var vm = Jobs.FirstOrDefault(j => j.Id == job.Id);
            if (vm is null)
            {
                vm = new RenderJobViewModel(job);
                if (_queue.ProgressOf(job.Id) is { } p && job.IsActive) vm.Apply(p);
                Jobs.Add(vm);
            }
            else if (vm.Job != job)
            {
                vm.Job = job;
            }
            if (FollowJob == job.Id && !job.IsActive) FollowedJobFinished?.Invoke(this, job);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Cancel(RenderJobViewModel? job)
    {
        if (job is not null) _queue.Cancel(job.Id);
    }

    [RelayCommand]
    private void Resume(RenderJobViewModel? job)
    {
        if (job is not null) _queue.Resume(job.Id);
    }

    [RelayCommand]
    private void Remove(RenderJobViewModel? job)
    {
        if (job is not null) _queue.Remove(job.Id);
    }

    [RelayCommand]
    private void ClearFinished() => _queue.ClearFinished();

    [RelayCommand]
    private void OpenFolder(RenderJobViewModel? job)
    {
        var file = job?.Job.Files.FirstOrDefault() ?? job?.Job.OutputPath;
        if (file is null) return;
        if (File.Exists(file))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = false });
        else if (Path.GetDirectoryName(file) is { } dir && System.IO.Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = false });
    }

    [RelayCommand]
    private void OpenFile(RenderJobViewModel? job)
    {
        if (job?.Job.Files.FirstOrDefault() is { } file && File.Exists(file))
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    }

    public void Dispose()
    {
        _queue.Changed -= OnChanged;
        _queue.ProgressChanged -= OnProgress;
    }
}
