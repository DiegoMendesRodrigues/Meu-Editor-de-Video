// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
namespace MeuEditorDeVideo.Presentation.Media;

/// <summary>Ponto em coordenadas de tela ou de imagem (substitui o <c>Point</c> do toolkit nos ViewModels).</summary>
public readonly record struct PointD(double X, double Y);
