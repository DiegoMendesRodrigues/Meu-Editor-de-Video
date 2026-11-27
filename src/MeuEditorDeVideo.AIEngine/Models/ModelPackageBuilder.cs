// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.AIEngine.Models;

/// <summary>
/// Gera um pacote <c>.medmodel</c> a partir de um arquivo de modelo e da entrada do catálogo — o mesmo formato de
/// <c>tools\New-MedModel.ps1</c>: manifest.json, o modelo, LICENSE.txt e README.md na raiz do zip.
/// </summary>
public static class ModelPackageBuilder
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static ModelManifest Manifest(ModelCatalogEntry entry, long sizeBytes, string sha256) => new()
    {
        Id = entry.Id,
        Name = entry.Name,
        Version = entry.Version,
        Source = entry.Source,
        Capability = entry.Capability,
        Purpose = entry.Purpose,
        Format = Path.GetExtension(entry.File).Equals(".bin", StringComparison.OrdinalIgnoreCase) ? "GGML" : "ONNX",
        File = entry.File,
        SizeBytes = sizeBytes,
        MemoryMb = entry.MemoryMb,
        Cpu = true,
        Gpu = entry.Gpu,
        License = entry.License,
        LicenseFile = "LICENSE.txt",
        Sha256 = sha256,
        Documentation = "README.md",
        PackageFormat = IModelRegistry.SupportedPackageFormat,
        Parameters = entry.Parameters,
        ExtraFiles = entry.ExtraFiles.Select(f => new ModelFileInfo(f.File, f.Sha256.ToLowerInvariant(), f.SizeBytes)).ToList(),
    };

    /// <summary>
    /// Cria (ou substitui) o pacote em <paramref name="output"/> e devolve o manifesto gravado. Os arquivos adicionais da
    /// entrada (<see cref="ModelCatalogEntry.ExtraFiles"/>) são lidos da mesma pasta de <paramref name="modelFile"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">Algum arquivo do modelo não confere com o catálogo.</exception>
    public static async Task<ModelManifest> BuildAsync(ModelCatalogEntry entry, string modelFile, string licenseFile, string output,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(modelFile);
        if (!info.Exists) throw new FileNotFoundException("Arquivo do modelo não encontrado.", modelFile);
        if (!File.Exists(licenseFile)) throw new FileNotFoundException("Licença do modelo não encontrada.", licenseFile);
        var sha = await CheckAsync(entry, modelFile, entry.SizeBytes, entry.Sha256, cancellationToken);
        var folder = Path.GetDirectoryName(Path.GetFullPath(modelFile))!;
        foreach (var extra in entry.ExtraFiles)
            await CheckAsync(entry, Path.Combine(folder, extra.File), extra.SizeBytes, extra.Sha256, cancellationToken);

        var manifest = Manifest(entry, info.Length, sha);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var temp = output + ".tmp";
        File.Delete(temp);
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            WriteText(zip, "manifest.json", ModelManifestJson.Serialize(manifest));
            // O modelo já é comprimido (pesos); "sem compressão" economiza tempo sem aumentar o pacote.
            zip.CreateEntryFromFile(modelFile, entry.File, CompressionLevel.NoCompression);
            foreach (var extra in entry.ExtraFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                zip.CreateEntryFromFile(Path.Combine(folder, extra.File), extra.File, CompressionLevel.NoCompression);
            }
            zip.CreateEntryFromFile(licenseFile, "LICENSE.txt", CompressionLevel.Optimal);
            WriteText(zip, "README.md", Readme(entry));
        }
        File.Move(temp, output, overwrite: true);
        return manifest;
    }

    private static async Task<string> CheckAsync(ModelCatalogEntry entry, string file, long size, string expectedSha, CancellationToken ct)
    {
        var info = new FileInfo(file);
        if (!info.Exists) throw new FileNotFoundException("Arquivo do modelo não encontrado.", file);
        string sha;
        await using (var stream = File.OpenRead(file))
            sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        if (info.Length != size || !string.Equals(sha, expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"O arquivo de '{entry.Name}' não confere com o catálogo (tamanho ou SHA-256 diferente).");
        return sha;
    }

    /// <summary>Documentação em pt-BR gerada a partir do catálogo.</summary>
    public static string Readme(ModelCatalogEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(entry.Name).AppendLine();
        sb.Append("**Finalidade:** ").AppendLine(entry.Purpose).AppendLine();
        if (entry.Features.Count > 0)
        {
            sb.AppendLine("## Recursos do editor").AppendLine();
            foreach (var f in entry.Features) sb.Append("- ").AppendLine(f);
            sb.AppendLine();
        }
        sb.Append("**Origem:** ").AppendLine(entry.Source).AppendLine();
        sb.Append("**Versão:** ").AppendLine(entry.Version).AppendLine();
        sb.Append("**Licença:** ").AppendLine(entry.License).AppendLine();
        sb.Append("**Arquivo original:** ").AppendLine(entry.Url.ToString()).AppendLine();
        if (entry.ExtraFiles.Count > 0)
        {
            sb.AppendLine("**Outros arquivos:**").AppendLine();
            foreach (var f in entry.ExtraFiles) sb.Append("- `").Append(f.File).Append("`: ").AppendLine(f.Url.ToString());
            sb.AppendLine();
        }
        sb.AppendLine("## Requisitos").AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"- Memória estimada: {entry.MemoryMb} MB").AppendLine();
        sb.Append("- Usa GPU: ").AppendLine(entry.Gpu ? "sim (DirectML no Windows, Core ML no macOS), com retorno para CPU" : "não (somente CPU)");
        if (entry.DependsOn.Count > 0) sb.Append("- Instalado junto com: ").AppendLine(string.Join(", ", entry.DependsOn));
        sb.AppendLine().AppendLine("## Parâmetros").AppendLine();
        if (entry.Parameters.Count == 0) sb.AppendLine("- (nenhum)");
        foreach (var (k, v) in entry.Parameters) sb.Append("- `").Append(k).Append("`: ").AppendLine(v);
        sb.AppendLine().AppendLine("## Instalação").AppendLine();
        sb.AppendLine("Pacote gerado pelo Meu Editor de Vídeo (Ferramentas › Modelos de IA › Baixar modelos). Pode ser copiado e");
        sb.AppendLine("instalado em outro computador sem internet por \"Instalar pacote…\".");
        return sb.ToString();
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), Utf8);
        writer.Write(text);
    }
}
