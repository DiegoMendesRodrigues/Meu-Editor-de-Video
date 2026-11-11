# Meu Editor de Vídeo

Editor de vídeo desktop para **Windows 11 (64 bits)**, em **Português do Brasil**, com processamento 100% local
(inclusive IA). Licenciado sob **GPL-3.0-or-later** ([`LICENSE.md`](LICENSE.md)).

- Especificação: [`ESPECIFICACAO_TECNICA_E_FUNCIONAL.zip`](ESPECIFICACAO_TECNICA_E_FUNCIONAL.zip)
- Plano de implantação: [`documentation/PLANO_DE_IMPLANTACAO.md`](documentation/PLANO_DE_IMPLANTACAO.md)
- Manual do usuário: [`documentation/MANUAL_DO_USUARIO.md`](documentation/MANUAL_DO_USUARIO.md) (também em **Ajuda › Manual do usuário**, F1)
- Documentação técnica: [`documentation/DOCUMENTACAO_TECNICA.md`](documentation/DOCUMENTACAO_TECNICA.md)
- Instalador e assinatura de código: [`documentation/INSTALADOR.md`](documentation/INSTALADOR.md)
- Publicação na Microsoft Store: [`documentation/MICROSOFT_STORE.md`](documentation/MICROSOFT_STORE.md)
- Homologação (CA001–CA024): [`documentation/HOMOLOGACAO.md`](documentation/HOMOLOGACAO.md)
- Dependências e licenças: [`documentation/DEPENDENCIAS.md`](documentation/DEPENDENCIAS.md) e [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt)

## Recursos

- Edição não linear com várias trilhas de vídeo, áudio, texto e imagem; dividir e **juntar os cortes**, recortar,
  copiar, colar no cursor, duplicar, excluir, excluir e fechar o espaço e **excluir e juntar** (em todas as trilhas).
- Editor com barra de título própria, biblioteca em abas, painel Propriedades em altura total e cor por clipe.
- **Visualizar**: modos de layout (Padrão, Linha do tempo, Vídeo), qualidade de reprodução, zoom no vídeo (Ajuste
  a 400%), reproduzir/pausar, parar, reprodução em loop e régua em pixels no monitor.
- Efeitos com miniaturas, predefinições de cor, ajustes HSL por faixa de cor e HDR; transições, máscaras,
  rastreamento de movimento e estabilização.
- IA local (ONNX Runtime, DirectML/CPU): remoção de fundo (MODNet), rastreamento de câmera com corte automático,
  **Retocar** (beleza e maquiagem com o Face Mesh V2) e legendas com Whisper.
- Exportação com FFmpeg (NVENC/AMF/QSV/software), fila de renderização e modelos de exportação.

## Requisitos de desenvolvimento

- Windows 11 x64
- .NET SDK 10.0.4xx
- PowerShell 7 (`pwsh`)

## Compilar e testar

```powershell
pwsh scripts/fetch-deps.ps1        # baixa e verifica FFmpeg, MODNet e Visual C++ Redistributable (SHA-256)
dotnet build MeuEditorDeVideo.sln
dotnet test MeuEditorDeVideo.sln
dotnet run --project src/MeuEditorDeVideo.App
```

## Instalador

```powershell
pwsh scripts/build-installer.ps1   # artifacts\installer\MeuEditorDeVideo-<versão>-Setup.exe e o .msi
pwsh scripts/build-msix.ps1        # artifacts\store\MeuEditorDeVideo_<versão>_x64.msix (Microsoft Store)
```

Detalhes (conteúdo, instalação silenciosa, desinstalação e assinatura de código) em
[`documentation/INSTALADOR.md`](documentation/INSTALADOR.md).

## Estrutura

```
src/        App (WPF/MVVM), Application, Domain, Core, Infrastructure e motores
            (Project, Media, Timeline, Image, Audio, AI, Rendering)
tests/      UnitTests, IntegrationTests, MediaTests
resources/  modelos de exportação, transições, efeitos, LUTs, fontes
scripts/    dependências, avisos de terceiros, instalador e bateria offline
installer/  instalador WiX: Msi/ (MSI) e Bundle/ (Setup.exe com o Visual C++); Store/ (MSIX)
documentation/
```

## Privacidade

Todo o processamento (inclusive a IA) é local, sem telemetria. A única exceção de rede é a ferramenta
**Baixar modelos de IA**: quando o usuário clica em **Instalar**, ela baixa os modelos opcionais das URLs oficiais
fixadas no catálogo, só por HTTPS e com verificação SHA-256. Esse código fica isolado no assembly
`MeuEditorDeVideo.ModelDownloads`. Testes automatizados garantem que nenhum outro assembly usa rede
(`tests/MeuEditorDeVideo.UnitTests/Architecture/PrivacyTests.cs`). A bateria `pwsh scripts/offline-check.ps1`
confirma em execução que o aplicativo não abre conexões.
