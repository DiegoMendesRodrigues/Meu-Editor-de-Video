// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.Presentation.Media;

/// <summary>
/// Quadro para o monitor. Cada apresentação cria uma instância nova (o motor pode reaproveitar o buffer de pixels),
/// e a view copia os pixels para a superfície dela no momento em que a propriedade muda, na thread da interface.
/// </summary>
public sealed class DisplayFrame(BgraImage image)
{
    public BgraImage Image { get; } = image;

    public int Width => Image.Width;

    public int Height => Image.Height;
}
