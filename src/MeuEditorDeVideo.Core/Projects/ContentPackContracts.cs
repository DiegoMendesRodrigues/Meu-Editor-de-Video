using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Core.Projects;

/// <summary>Pacote de conteúdo instalado (pasta local com figurinhas, títulos e modelos de projeto).</summary>
public sealed record ContentPackInfo(string Id, string Name, string Folder, int Stickers, int Titles, int Templates)
{
    /// <summary>O pacote onde ficam os títulos e modelos salvos pelo próprio usuário.</summary>
    public bool IsUserPack { get; init; }

    /// <summary>Vem com o programa (pasta <c>pacotes</c> ao lado do executável): só leitura, não pode ser removido.</summary>
    public bool IsBuiltIn { get; init; }
}

/// <summary>Item de um pacote: figurinha (imagem), título (.medtitle) ou modelo de projeto (.med).</summary>
public sealed record ContentPackItem(string PackId, string PackName, string Name, string Path)
{
    /// <summary>Nome lido por leitores de tela (o item da lista usa o texto do objeto).</summary>
    public override string ToString() => $"{Name} ({PackName})";
}

/// <summary>
/// Pacotes locais de figurinhas, títulos e modelos de projeto (sem rede). Um pacote é um arquivo .medpack (ZIP) com as pastas
/// <c>figurinhas/</c>, <c>titulos/</c> e <c>modelos/</c> e, opcionalmente, <c>pacote.json</c> com o nome.
/// </summary>
public interface IContentPackStore
{
    IReadOnlyList<ContentPackInfo> Packs();

    IReadOnlyList<ContentPackItem> Stickers();

    IReadOnlyList<ContentPackItem> Titles();

    IReadOnlyList<ContentPackItem> Templates();

    /// <summary>Instala (ou atualiza) um pacote .medpack/.zip.</summary>
    /// <exception cref="InvalidDataException">Arquivo inválido (mensagem para o usuário).</exception>
    ContentPackInfo Install(string packagePath);

    /// <summary>Gera um .medpack com o conteúdo de um pacote instalado (para compartilhar).</summary>
    void Export(string packId, string destinationPath);

    /// <exception cref="InvalidOperationException">O pacote vem com o programa (mensagem para o usuário).</exception>
    void Remove(string packId);

    /// <summary>Salva um clipe de texto como título reutilizável no pacote do usuário.</summary>
    ContentPackItem SaveTitle(Clip textClip, string name);

    /// <summary>Lê um título salvo (clipe de texto, com novo Id e começando em zero).</summary>
    /// <exception cref="InvalidDataException">Arquivo inválido.</exception>
    Clip LoadTitle(string path);

    /// <summary>Caminho onde gravar um novo modelo de projeto do usuário com este nome (.med).</summary>
    string NewTemplatePath(string name);
}
