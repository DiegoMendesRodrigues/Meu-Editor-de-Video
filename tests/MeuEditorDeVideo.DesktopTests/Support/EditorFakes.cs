using MeuEditorDeVideo.Presentation.Media;
using MeuEditorDeVideo.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.DesktopTests.Support;

/// <summary>
/// Editor completo (ViewModel real do editor, com os serviços reais do aplicativo e sem janelas) sobre o projeto de
/// exemplo do <see cref="TimelineFixture"/>: "Praia.mp4" e "Floresta Amazônica.mp4" na linha do tempo e na biblioteca.
/// </summary>
internal sealed class EditorFixture : IDisposable
{
    private readonly TimelineFixture _base = new();

    public EditorFixture()
    {
        Editor = _base.Services.GetRequiredService<EditorViewModel>();
        Editor.OnNavigatedTo();
        // Miniaturas e formas de onda da biblioteca (a leitura real das mídias não roda nos testes).
        foreach (var id in new[] { _base.Beach, _base.Forest })
            Editor.Library!.Find(id)?.SetPreview(TimelineFixture.Gradient(160, 90, id == _base.Beach ? 0 : 1), TimelineFixture.Sine(id == _base.Beach ? 12 : 20));
        Editor.Timeline!.ViewportWidth = 900;
        Editor.Timeline.SetZoom(60, 0);
        Editor.Preview!.Image = new DisplayFrame(TimelineFixture.Gradient(640, 360, 0));
    }

    public EditorViewModel Editor { get; }

    public IServiceProvider Services => _base.Services;

    public Guid BeachClip => _base.BeachClip;

    public Guid ForestClip => _base.ForestClip;

    public Guid ForestAudioClip => _base.ForestAudioClip;

    public void Dispose()
    {
        Editor.OnNavigatedFrom();
        try
        {
            _base.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // A biblioteca de mídias já foi liberada por OnNavigatedFrom e o host a libera de novo.
        }
    }
}
