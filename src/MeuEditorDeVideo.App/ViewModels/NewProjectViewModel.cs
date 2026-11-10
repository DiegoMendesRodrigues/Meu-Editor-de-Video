using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.App.ViewModels;

public sealed record Option<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public sealed record ResolutionOption(string Label, int Width, int Height)
{
    public bool IsCustom => Width == 0;
    public override string ToString() => Label;
}

public sealed record AspectOption(string Label, int W, int H, IReadOnlyList<ResolutionOption> Resolutions)
{
    public override string ToString() => Label;
}

/// <summary>Modelo de projeto: preenche proporção, resolução e FPS (ícones genéricos, sem marcas).</summary>
public sealed record ProjectTemplate(string Label, string Description, int Width, int Height, FrameRate FrameRate, string Group = "Geral")
{
    public bool IsCustom => Width == 0;
    public override string ToString() => Label;
}

/// <summary>Tela de criação de projeto (§5.2).</summary>
public sealed partial class NewProjectViewModel : ObservableObject
{
    private static readonly ResolutionOption Custom = new("Personalizada", 0, 0);
    private readonly IDialogService _dialogs;
    private bool _applyingTemplate;

    public NewProjectViewModel(IDialogService dialogs, ISettingsService settings)
    {
        _dialogs = dialogs;
        Directory = settings.Current.DefaultProjectsDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Meu Editor de Vídeo");
        SelectedTemplate = Templates[0];
    }

    public IReadOnlyList<ProjectTemplate> Templates { get; } =
    [
        new("Vídeo horizontal Full HD", "1920 × 1080 · 30 FPS · plataformas de vídeo", 1920, 1080, FrameRate.Fps30),
        new("Vídeo horizontal 4K", "3840 × 2160 · 30 FPS", 3840, 2160, FrameRate.Fps30),
        new("Vídeo vertical", "1080 × 1920 · 30 FPS · vídeos curtos para celular", 1080, 1920, FrameRate.Fps30),
        new("Vídeo quadrado", "1080 × 1080 · 30 FPS · redes sociais", 1080, 1080, FrameRate.Fps30),
        new("Publicação retrato", "1080 × 1350 · 30 FPS · redes sociais", 1080, 1350, FrameRate.Fps30),
        new("Cinema", "3840 × 2160 · 24 FPS", 3840, 2160, FrameRate.Fps24),
        // Internet: mesmas resoluções dos modelos de exportação (resources/templates/export).
        new("YouTube", "1920 × 1080 · 30 FPS · vídeo horizontal 16:9", 1920, 1080, FrameRate.Fps30, "Internet e redes sociais"),
        new("YouTube 4K", "3840 × 2160 · 30 FPS · vídeo horizontal 16:9", 3840, 2160, FrameRate.Fps30, "Internet e redes sociais"),
        new("YouTube Shorts", "1080 × 1920 · 30 FPS · vertical 9:16", 1080, 1920, FrameRate.Fps30, "Internet e redes sociais"),
        new("Instagram Reels", "1080 × 1920 · 30 FPS · vertical 9:16", 1080, 1920, FrameRate.Fps30, "Internet e redes sociais"),
        new("Instagram Stories", "1080 × 1920 · 30 FPS · vertical 9:16", 1080, 1920, FrameRate.Fps30, "Internet e redes sociais"),
        new("Instagram Feed", "1080 × 1350 · 30 FPS · retrato 4:5", 1080, 1350, FrameRate.Fps30, "Internet e redes sociais"),
        new("Instagram Feed quadrado", "1080 × 1080 · 30 FPS · quadrado 1:1", 1080, 1080, FrameRate.Fps30, "Internet e redes sociais"),
        new("TikTok", "1080 × 1920 · 30 FPS · vertical 9:16", 1080, 1920, FrameRate.Fps30, "Internet e redes sociais"),
        new("Kwai", "1080 × 1920 · 30 FPS · vertical 9:16", 1080, 1920, FrameRate.Fps30, "Internet e redes sociais"),
        new("WhatsApp Status", "720 × 1280 · 30 FPS · vertical 9:16, arquivo leve", 720, 1280, FrameRate.Fps30, "Internet e redes sociais"),
        new("WhatsApp compartilhamento", "1280 × 720 · 30 FPS · horizontal, arquivo leve", 1280, 720, FrameRate.Fps30, "Internet e redes sociais"),
        new("Facebook Feed", "1080 × 1080 · 30 FPS · quadrado 1:1", 1080, 1080, FrameRate.Fps30, "Internet e redes sociais"),
        new("Facebook Reels", "1080 × 1920 · 30 FPS · vertical 9:16", 1080, 1920, FrameRate.Fps30, "Internet e redes sociais"),
        new("Facebook Stories", "1080 × 1920 · 30 FPS · vertical 9:16", 1080, 1920, FrameRate.Fps30, "Internet e redes sociais"),
        new("LinkedIn", "1920 × 1080 · 30 FPS · horizontal 16:9", 1920, 1080, FrameRate.Fps30, "Internet e redes sociais"),
        new("X (Twitter)", "1920 × 1080 · 30 FPS · horizontal 16:9", 1920, 1080, FrameRate.Fps30, "Internet e redes sociais"),
        new("Pinterest", "1000 × 1500 · 30 FPS · retrato 2:3", 1000, 1500, FrameRate.Fps30, "Internet e redes sociais"),
        new("Personalizado", "Defina todas as opções manualmente", 0, 0, FrameRate.Fps30, "Personalizado"),
    ];

    public IReadOnlyList<AspectOption> Aspects { get; } =
    [
        new("16:9 (horizontal)", 16, 9, [R(1280, 720), R(1920, 1080), R(2560, 1440), R(3840, 2160), Custom]),
        new("9:16 (vertical)", 9, 16, [R(720, 1280), R(1080, 1920), R(1440, 2560), R(2160, 3840), Custom]),
        new("1:1 (quadrado)", 1, 1, [R(720, 720), R(1080, 1080), R(2160, 2160), Custom]),
        new("4:5 (retrato)", 4, 5, [R(1080, 1350), Custom]),
        new("4:3", 4, 3, [R(1024, 768), R(1440, 1080), Custom]),
        new("21:9 (ultra-wide)", 21, 9, [R(2560, 1080), R(3440, 1440), Custom]),
        new("Personalizada", 0, 0, [Custom]),
    ];

    public IReadOnlyList<Option<FrameRate>> FrameRates { get; } =
    [
        new("23,976", FrameRate.Fps23_976),
        new("24", FrameRate.Fps24),
        new("25", FrameRate.Fps25),
        new("29,97", FrameRate.Fps29_97),
        new("30", FrameRate.Fps30),
        new("50", FrameRate.Fps50),
        new("59,94", FrameRate.Fps59_94),
        new("60", FrameRate.Fps60),
    ];

    public IReadOnlyList<Option<ColorProfile>> ColorProfiles { get; } =
    [
        new("Rec. 709 (padrão para vídeo)", ColorProfile.Rec709),
        new("sRGB", ColorProfile.SRgb),
        new("Rec. 2020", ColorProfile.Rec2020),
    ];

    public IReadOnlyList<Option<StorageMode>> StorageModes { get; } =
    [
        new("Vincular mídias (arquivos ficam onde estão)", StorageMode.Linked),
        new("Empacotar mídias no projeto (.med independente)", StorageMode.Packaged),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Name { get; set; } = "Novo projeto";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Directory { get; set; }

    [ObservableProperty]
    public partial ProjectTemplate SelectedTemplate { get; set; }

    [ObservableProperty]
    public partial AspectOption SelectedAspect { get; set; } = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomResolution))]
    public partial ResolutionOption SelectedResolution { get; set; } = null!;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string CustomWidth { get; set; } = "1920";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string CustomHeight { get; set; } = "1080";

    [ObservableProperty]
    public partial Option<FrameRate> SelectedFrameRate { get; set; } = null!;

    [ObservableProperty]
    public partial Option<ColorProfile> SelectedColorProfile { get; set; } = null!;

    [ObservableProperty]
    public partial Option<StorageMode> SelectedStorageMode { get; set; } = null!;

    /// <summary>Mensagem de validação exibida abaixo do formulário (vazia se tudo certo).</summary>
    [ObservableProperty]
    public partial string ValidationMessage { get; set; } = "";

    public bool IsCustomResolution => SelectedResolution?.IsCustom == true;

    /// <summary>Caminho final do arquivo, exibido como dica.</summary>
    public string TargetPath => Path.Combine(Directory ?? "", (Name ?? "").Trim() + ".med");

    /// <summary>Preenchido quando o usuário confirma.</summary>
    public NewProjectRequest? Result { get; private set; }

    /// <summary>Fecha a janela com o resultado (true = criar).</summary>
    public event EventHandler<bool>? CloseRequested;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(TargetPath));

    partial void OnDirectoryChanged(string value) => OnPropertyChanged(nameof(TargetPath));

    partial void OnSelectedTemplateChanged(ProjectTemplate value)
    {
        if (value is null || value.IsCustom) return;
        _applyingTemplate = true;
        try
        {
            var aspect = AspectRatio.FromSize(value.Width, value.Height);
            SelectedAspect = Aspects.FirstOrDefault(a => a.W == aspect.Width && a.H == aspect.Height) ?? Aspects[^1];
            SelectedResolution = SelectedAspect.Resolutions.FirstOrDefault(r => r.Width == value.Width && r.Height == value.Height) ?? Custom;
            CustomWidth = value.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            CustomHeight = value.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SelectedFrameRate = FrameRates.First(f => f.Value == value.FrameRate);
            SelectedColorProfile ??= ColorProfiles[0];
            SelectedStorageMode ??= StorageModes[0];
        }
        finally
        {
            _applyingTemplate = false;
        }
    }

    partial void OnSelectedAspectChanged(AspectOption value)
    {
        if (_applyingTemplate || value is null) return;
        // Mantém a altura mais próxima da escolhida antes, se possível.
        var currentHeight = SelectedResolution is { IsCustom: false } r ? Math.Min(r.Width, r.Height) : 1080;
        SelectedResolution = value.Resolutions
            .Where(x => !x.IsCustom)
            .OrderBy(x => Math.Abs(Math.Min(x.Width, x.Height) - currentHeight))
            .FirstOrDefault() ?? Custom;
        MarkCustomTemplate();
    }

    partial void OnSelectedResolutionChanged(ResolutionOption value)
    {
        // A ComboBox anula a seleção ao trocar a lista de resoluções (mudança de proporção).
        if (value is null) return;
        if (!value.IsCustom)
        {
            CustomWidth = value.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            CustomHeight = value.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (!_applyingTemplate) MarkCustomTemplate();
    }

    partial void OnSelectedFrameRateChanged(Option<FrameRate> value)
    {
        if (value is not null && !_applyingTemplate) MarkCustomTemplate();
    }

    private void MarkCustomTemplate()
    {
        if (_applyingTemplate || SelectedTemplate is null || SelectedTemplate.IsCustom) return;
        var (w, h) = CurrentSize();
        if (w != SelectedTemplate.Width || h != SelectedTemplate.Height || SelectedFrameRate?.Value != SelectedTemplate.FrameRate)
        {
            _applyingTemplate = true;
            SelectedTemplate = Templates[^1];
            _applyingTemplate = false;
        }
    }

    private (int W, int H) CurrentSize()
    {
        int.TryParse(CustomWidth, out var w);
        int.TryParse(CustomHeight, out var h);
        return (w, h);
    }

    [RelayCommand]
    private void BrowseDirectory()
    {
        var folder = _dialogs.PickFolder("Escolha a pasta do projeto", System.IO.Directory.Exists(Directory) ? Directory : null);
        if (folder is not null) Directory = folder;
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Directory);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        var (w, h) = CurrentSize();
        var settings = new ProjectSettings
        {
            Width = w,
            Height = h,
            FrameRate = SelectedFrameRate.Value,
            ColorProfile = SelectedColorProfile.Value,
        };

        var problems = settings.Validate().ToList();
        var name = Name.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            problems.Insert(0, "O nome não pode conter os caracteres \\ / : * ? \" < > |");
        if (File.Exists(TargetPath))
            problems.Insert(0, "Já existe um projeto com esse nome nesta pasta.");

        if (problems.Count > 0)
        {
            ValidationMessage = string.Join("\n", problems);
            return;
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ValidationMessage = $"Não foi possível usar a pasta escolhida: {ex.Message}";
            return;
        }

        ValidationMessage = "";
        Result = new NewProjectRequest(name, Directory, settings, SelectedStorageMode.Value);
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private static ResolutionOption R(int w, int h) => new($"{w} × {h}", w, h);
}
