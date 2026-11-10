using MeuEditorDeVideo.AIEngine;
using MeuEditorDeVideo.AIEngine.Vision;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.IntegrationTests.Support;

namespace MeuEditorDeVideo.IntegrationTests;

/// <summary>
/// §14.2 (4): Motor de IA ↔ ONNX Runtime. Remoção de fundo com o MODNet incluído no aplicativo, pelo DI real.
/// O modelo vem da pasta do aplicativo: nenhum teste desta classe usa a rede. Sem o arquivo do modelo, os testes são ignorados.
/// </summary>
[Collection(IntegrationMediaCollection.Name)]
public sealed class AiOnnxIntegrationTests : IDisposable
{
    private readonly IntegrationMedia _media;
    private readonly AppContainer _app;

    public AiOnnxIntegrationTests(IntegrationMedia media)
    {
        _media = media;
        _app = new AppContainer(TempFiles.NewRoot("ia-onnx"));
    }

    private static BgraImage Quadro(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = 40;
            px[i + 1] = 120;
            px[i + 2] = 200;
            px[i + 3] = 255;
        }
        return new BgraImage(w, h, px);
    }

    private static MediaAsset ImagemComoAtivo(string arquivo) => new()
    {
        Id = Guid.NewGuid(), Kind = MediaKind.Image, Name = Path.GetFileName(arquivo), OriginalPath = arquivo,
        Width = 320, Height = 240,
    };

    [ModeloFact("modnet.onnx")]
    public void Modelo_incluido_no_aplicativo_e_registrado_e_disponivel_sem_rede()
    {
        // O registro procura o modelo na pasta do aplicativo (ai-models) e não tenta nenhum download.
        var registry = _app.Get<IModelRegistry>();
        var model = registry.Find(ModelCapability.PersonSegmentation);

        Assert.NotNull(model);
        Assert.True(model.BuiltIn);
        Assert.True(_app.Get<ISegmentationService>().IsAvailable);
    }

    [ModeloFact("modnet.onnx")]
    public void Remocao_de_fundo_gera_mascara_no_tamanho_de_inferencia_e_grava_no_cache()
    {
        var segmentation = _app.Get<ISegmentationService>();
        var cache = _app.Get<MatteCache>();
        var asset = ImagemComoAtivo(_media.Image);

        var matte = segmentation.GetMatte(asset, _media.Image, MediaTime.Zero, Quadro(320, 240));

        Assert.NotNull(matte);
        Assert.Equal(0, matte.Width % 32);
        Assert.Equal(0, matte.Height % 32);
        Assert.Equal(matte.Width * matte.Height, matte.Alpha.Length);
        Assert.True(cache.Contains(asset.Id, 0));
        Assert.True(File.Exists(Path.Combine(cache.Directory!, asset.Id.ToString("N"), "0.png")));
    }

    [ModeloFact("modnet.onnx")]
    public void Segunda_consulta_da_mesma_mascara_vem_do_cache_com_o_mesmo_resultado()
    {
        var segmentation = _app.Get<ISegmentationService>();
        var asset = ImagemComoAtivo(_media.Image);
        var primeira = segmentation.GetMatte(asset, _media.Image, MediaTime.Zero, Quadro(320, 240))!;

        var segunda = segmentation.GetMatte(asset, null, MediaTime.Zero, Quadro(8, 8));

        Assert.NotNull(segunda);
        Assert.Equal(primeira.Alpha, segunda.Alpha);
    }

    public void Dispose() => _app.Dispose();
}
