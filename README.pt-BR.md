[English](README.md) | **Português (Brasil)**

<p align="center">
  <a href="https://learn.microsoft.com/dotnet/csharp/"><img alt="C#" src="https://img.shields.io/badge/C%23-14-68217A?style=for-the-badge&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0iI2ZmZiIgZD0iTTEyIDEgMi41IDYuNXYxMUwxMiAyM2w5LjUtNS41di0xMXoiLz48dGV4dCB4PSIxMiIgeT0iMTUuNSIgZm9udC1mYW1pbHk9IkFyaWFsLHNhbnMtc2VyaWYiIGZvbnQtc2l6ZT0iOC41IiBmb250LXdlaWdodD0iNzAwIiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjNjgyMTdBIj5DIzwvdGV4dD48L3N2Zz4=&logoColor=white"></a>
  <a href="https://dotnet.microsoft.com/"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"></a>
  <a href="#instalação"><img alt="Windows, Linux e macOS" src="https://img.shields.io/badge/Windows%20%C2%B7%20Linux%20%C2%B7%20macOS-plataformas-444?style=for-the-badge"></a>
  <a href="https://avaloniaui.net/"><img alt="Avalonia" src="https://img.shields.io/badge/Avalonia-Fluent-0C54C2?style=for-the-badge"></a>
  <a href="https://ffmpeg.org/"><img alt="FFmpeg 9" src="https://img.shields.io/badge/FFmpeg-9-007808?style=for-the-badge&logo=ffmpeg&logoColor=white"></a>
  <a href="https://github.com/naudio/NAudio"><img alt="NAudio 2.2" src="https://img.shields.io/badge/NAudio-2.2-E65100?style=for-the-badge&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0iI2ZmZiIgZD0iTTMgOXY2aDRsNSA1VjRMNyA5em0xMy41IDNBNC41IDQuNSAwIDAgMCAxNCA4djhhNC41IDQuNSAwIDAgMCAyLjUtNHpNMTQgMy4ydjIuMWE3IDcgMCAwIDEgMCAxMy40djIuMWE5IDkgMCAwIDAgMC0xNy42eiIvPjwvc3ZnPg==&logoColor=white"></a>
  <a href="https://github.com/shimat/opencvsharp"><img alt="OpenCvSharp 4.13" src="https://img.shields.io/badge/OpenCvSharp-4.13-5C3EE8?style=for-the-badge&logo=opencv&logoColor=white"></a>
  <a href="https://onnxruntime.ai/"><img alt="ONNX Runtime 1.24" src="https://img.shields.io/badge/ONNX%20Runtime-1.24-005CED?style=for-the-badge&logo=onnx&logoColor=white"></a>
  <a href="https://github.com/microsoft/onnxruntime-genai"><img alt="ONNX Runtime GenAI 0.14" src="https://img.shields.io/badge/ONNX%20Runtime%20GenAI-0.14-005CED?style=for-the-badge&logo=onnx&logoColor=white"></a>
  <a href="https://github.com/ZHKKKe/MODNet"><img alt="MODNet" src="https://img.shields.io/badge/MODNet-matting-0A7E8C?style=for-the-badge&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0iI2ZmZiIgZD0iTTEyIDEyYTUgNSAwIDEgMCAwLTEwIDUgNSAwIDAgMCAwIDEwem0wIDJjLTQuNCAwLTkgMi4yLTkgNXYzaDE4di0zYzAtMi44LTQuNi01LTktNXoiLz48L3N2Zz4=&logoColor=white"></a>
  <a href="CODE_OF_CONDUCT.md"><img alt="Contributor Covenant 3.0" src="https://img.shields.io/badge/Contributor%20Covenant-3.0-5E0D73?style=for-the-badge&logo=contributorcovenant&logoColor=white"></a>
</p>

# Meu Editor de Vídeo

Editor de vídeo não linear para **Windows 11 (64 bits)**, **Linux (64 bits)** e **macOS**, feito a partir de uma única
base de código, com interface e manual em **seis idiomas** e processamento 100% local, inclusive a inteligência
artificial. Nenhum vídeo, áudio, imagem ou texto sai do computador.

Versão **1.4.0**.

## Instalação

| Sistema | Pacote | Requisitos |
|---|---|---|
| Windows | `Setup.exe` (recomendado) ou `.msi` | Windows 11 64 bits (x64) |
| Linux | `.deb`: `sudo apt install ./meu-editor-de-video_<versão>_amd64.deb` | Debian 12+ ou Ubuntu 22.04+, x64 |
| macOS | `.dmg`: arraste o aplicativo para **Aplicativos** | macOS 12+, Apple Silicon (arm64) ou Intel (x64) |

Passo a passo, instalação silenciosa, desinstalação e o ambiente de desenvolvimento em cada sistema:
[`INSTALL.pt-BR.md`](INSTALL.pt-BR.md). Os limites que dependem do sistema (Wayland, Gatekeeper, IA em Macs com Intel)
estão em [Observações por sistema](#observações-por-sistema).

## Documentação

- **Instalação** (usuário final e ambiente de desenvolvimento): [`INSTALL.pt-BR.md`](INSTALL.pt-BR.md)
- Manual do usuário: [`documentation/MANUAL_DO_USUARIO.md`](documentation/MANUAL_DO_USUARIO.md) (também em
  **Ajuda › Manual do usuário**, F1; versões em [inglês](documentation/MANUAL_DO_USUARIO.en.md),
  [espanhol](documentation/MANUAL_DO_USUARIO.es.md), [francês](documentation/MANUAL_DO_USUARIO.fr.md),
  [alemão](documentation/MANUAL_DO_USUARIO.de.md) e [português de Portugal](documentation/MANUAL_DO_USUARIO.pt-PT.md))
- Documentação técnica: [`documentation/DOCUMENTACAO_TECNICA.md`](documentation/DOCUMENTACAO_TECNICA.md)
- Instalador e assinatura de código: [`documentation/INSTALADOR.md`](documentation/INSTALADOR.md)
- Publicação na Microsoft Store: [`documentation/MICROSOFT_STORE.md`](documentation/MICROSOFT_STORE.md)
- Modelos de IA: [`documentation/MODELOS_IA.md`](documentation/MODELOS_IA.md)
- Homologação (CA001–CA024): [`documentation/HOMOLOGACAO.md`](documentation/HOMOLOGACAO.md)
- Dependências e licenças: [`documentation/DEPENDENCIAS.md`](documentation/DEPENDENCIAS.md) e
  [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt)
- Política de privacidade: [`POLITICA_DE_PRIVACIDADE.md`](POLITICA_DE_PRIVACIDADE.md)
- Especificação: [`ESPECIFICACAO_TECNICA_E_FUNCIONAL.zip`](ESPECIFICACAO_TECNICA_E_FUNCIONAL.zip)

## Funcionalidades

### Interface e idiomas

- Interface e manual em **alemão, espanhol, francês, inglês, português (Brasil) e português (Portugal)**. Na primeira
  execução, a janela de **boas-vindas** pede nome, idioma, tema e se o programa deve verificar atualizações; trocar o
  idioma reinicia o programa nele.
- Abre direto no editor, com um projeto vazio; o menu **Arquivo** lista os cinco projetos recentes.
- Barra de título própria, temas **Claro**, **Escuro** e **Igual ao sistema**, biblioteca em abas e painel
  **Propriedades** em altura total; no macOS os menus viram a barra de menus nativa. No canto inferior direito, atalhos de um clique para o **Manual do usuário** e o
  **Assistente virtual**.
- **Visualizar**: modos de layout (Padrão, Linha do tempo, Vídeo), qualidade da reprodução, zoom do monitor (Ajuste a
  400%), reprodução em loop e régua em pixels.
- Atalhos de teclado configuráveis, com aviso de conflito. No macOS usam **Cmd** e **Option** no lugar de Ctrl e Alt,
  em todos os idiomas.
- Os diálogos cabem na área útil de telas pequenas: encolhem, o conteúdo rola e os botões ficam sempre à vista.
- Acessibilidade: nomes para leitores de tela (UI Automation, NSAccessibility, AT-SPI) em controles, listas e grades.

### Mídia e projetos

- Importa vídeo (MP4, MOV, MKV, WebM, AVI, MXF e outros), áudio (MP3, WAV, AAC, FLAC, Opus e outros), imagem (JPG, PNG,
  WebP, TIFF, **SVG** e outros), GIF animado e sequência de imagens numeradas.
- **Proxies** de 540p para pré-visualizar vídeos pesados; a exportação usa sempre o original.
- Projetos `.med` vinculados ou com as mídias **empacotadas**, gravação atômica com verificação SHA-256, **salvamento
  automático** e recuperação após travamento, cópias de segurança e localização de mídias ausentes.
- **Pacotes locais** (`.medpack`) de figurinhas, títulos e modelos de projeto, com o pacote **Básico** de 40
  figurinhas incluído.

### Linha do tempo

- Várias trilhas de vídeo, áudio, texto, imagem e legenda; trilhas bloqueadas, ocultas e silenciadas.
- Dividir e **juntar os cortes**, aparar, copiar, colar no cursor, duplicar, excluir, **excluir e fechar o espaço** e
  **excluir e juntar** (em todas as trilhas).
- **Ímã de alinhamento**, **edição em cascata (ripple)**, agrupar e vincular clipes, cor por clipe, marcadores, zoom e
  desfazer/refazer com histórico.
- **Velocidade** constante e em curva, **reproduzir ao contrário**, quadro congelado e manter o tom da voz.
- **Camadas de ajuste**, **clipes compostos**, **tela dividida** com modelos, **multicâmera** (Alt+1 a Alt+4) e clipes
  desativados.

### Imagem, cor e composição

- Posição, escala, rotação, opacidade, **perspectiva 2.5D**, modos de mistura e keyframes em todas as propriedades.
- **Recorte** e **Aplicar recorte ao vídeo** (o projeto e a exportação passam a ter o tamanho recortado).
- **Reenquadrar automaticamente** para 9:16, 1:1, 4:5 ou 16:9, acompanhando o rosto, o corpo ou o centro do quadro.
- Efeitos com miniaturas: desfoque, nitidez, vinheta, granulado, **chroma key**, **refinar bordas**, **reenquadrar
  360°**, **correção de lente**, **redução de ruído de vídeo**, **visualizador de áudio** e outros; comparação
  antes/depois no monitor.
- Cor: **predefinições**, ajustes de cor, balanço de branco, **curvas**, **rodas de cor**, **LUT 3D (.cube)**, **HSL**
  por faixa de cor, **HDR** e **combinar cor** entre clipes.
- **Máscaras** (retângulo, elipse, livre, com forma-chave), máscaras só para efeitos, **rastreamento de movimento** e
  **estabilização**.
- Elementos gráficos (formas e SVG), **textos** com modelos (título, tarja, créditos rolantes), fontes próprias,
  animações de entrada e saída e títulos salvos.
- **Transições** entre clipes.

### Áudio

- **Mixer** com volume, panorâmica, mudo, solo e medidores de nível.
- Keyframes de volume, fades, **normalização de loudness** (EBU R128), **remover silêncios**, **marcar batidas** e
  **cortar vídeos nas batidas**, **sincronizar pelo áudio** e **atenuação automática** da música sob a fala.
- Efeitos: equalizador, compressor, redução de ruído, eco, reverberação, telefone/rádio, distorção e **mudar a voz**.

### Inteligência artificial (local)

- **Remover fundo** (MODNet, incluído) com fundo transparente, cor, desfoque ou outra mídia; máscaras calculadas em
  segundo plano e **rastrear pessoa**.
- **Gerar legendas** a partir da fala (Whisper), **isolar voz** e **reduzir ruído** (GTCRN), **remover trechos sem
  fala** (Silero VAD), **detectar cenas** (TransNetV2), **melhorar imagem** 2× (Swin2SR) e **retocar** rostos com
  beleza, maquiagem e desfoque (MediaPipe Face Mesh V2).
- **Assistente virtual** (Ajuda › Assistente virtual, Shift+F1): responde dúvidas sobre o programa e a edição de vídeo
  a partir do manual, no idioma da interface. Com o modelo opcional **Phi-4-mini** instalado, escreve a resposta em
  linguagem natural e cita a seção do manual; sem ele, mostra as seções encontradas.
- Os modelos opcionais são baixados uma única vez, pelo próprio programa, das origens oficiais (HTTPS e SHA-256).
  Rodam na placa de vídeo (DirectML no Windows, Core ML nos Macs com Apple Silicon) ou no processador (Linux e Macs
  com Intel). **Macs com Intel não têm ONNX Runtime**: ali os recursos que dependem dele (como remover fundo e o
  assistente virtual) ficam desativados, com aviso, e só as legendas com Whisper funcionam.

### Gravação e exportação

- **Gravar tela, webcam e microfone** ao mesmo tempo, com escolha do monitor, webcam num canto, contagem regressiva e
  **F10** para parar. No Linux a tela é gravada no Xorg (X11); no Wayland a gravação de tela fica indisponível, e a
  webcam e o microfone continuam funcionando.
- **Texto para fala** com as vozes do sistema (SAPI no Windows, `say` no macOS e `espeak-ng` no Linux).
- **Exportar** com modelos para YouTube, Shorts, Instagram, TikTok, WhatsApp, Facebook e outros, além de modelos
  próprios; MP4, MOV, MKV, WebM, AVI, MPEG-2 e GIF; H.264, H.265, AV1, VP9, ProRes, FFV1 e outros; codificação pela
  placa de vídeo (NVENC, AMF e QSV no Windows, NVENC e QSV no Linux, VideoToolbox no macOS) ou por software; canal alfa, intervalo, divisão em partes e legendas (SRT/VTT ou
  queimadas).
- **Fila de renderização** em segundo plano, com retomada de exportações interrompidas.
- **Exportar o quadro atual** em PNG ou JPEG.
- Legendas: importar e exportar SRT/VTT e editor de legendas.

### Configurações e privacidade

- Detecção do hardware com recomendações, qualidade da pré-visualização, proxies automáticos, aceleração pela placa de
  vídeo, dispositivo da IA, limite e limpeza do cache.
- **Sem telemetria.** A rede é usada só para **Verificar atualizações** (envia apenas o idioma da interface e o sistema operacional) e para
  baixar os modelos de IA quando o usuário clica em **Instalar**. Testes automatizados garantem que nenhuma outra parte
  do programa acessa a rede, e arquivos de terceiros (vídeos, SVG, projetos baixados) não conseguem abrir conexões.

## Tecnologias

| Área | Tecnologia |
|---|---|
| Plataforma | C# e .NET 10 (`net10.0`), publicado *self-contained* para `win-x64`, `linux-x64`, `osx-arm64` e `osx-x64` a partir de uma única base de código |
| Interface | Avalonia 12 com o tema Fluent, MVVM com CommunityToolkit.Mvvm 8.4 |
| Composição e serviços | Microsoft.Extensions.Hosting 10 (injeção de dependências), Serilog (logs locais em arquivo) |
| Vídeo e áudio | FFmpeg 9 (build compartilhado) via FFmpeg.AutoGen 9 (decodificação, codificação e mux); a ferramenta `ffmpeg` grava a tela (gdigrab e DirectShow no Windows, x11grab no Linux, avfoundation no macOS) |
| Reprodução de áudio | SDL3 (Linux e macOS) e NAudio 2.2 (WASAPI, Windows), relógio mestre da pré-visualização |
| Imagem e visão | OpenCvSharp 4.13 (rastreamento e estabilização), SkiaSharp 4 e HarfBuzz (texto e formas), Svg.Skia 5.2 (SVG), compositor próprio em CPU (2.5D) |
| IA | ONNX Runtime 1.24 com DirectML (GPU no Windows), Core ML (Apple Silicon) ou CPU (não existe nos Macs com Intel): MODNet, MediaPipe Face Mesh V2, Silero VAD, GTCRN, TransNetV2, Swin2SR; Whisper.net 1.9 (legendas) |
| Assistente virtual | Base de conhecimento dos seis manuais com busca BM25 local; ONNX Runtime GenAI 0.14 (DirectML no Windows, CPU nos demais) com Phi-4-mini-instruct int4 |
| Formato de projeto | `.med` (ZIP com `manifest.json` e `project.json`, versionado com migrações), tempo em *flicks* (`long`) |
| Idiomas | Tradução da árvore visual em tempo de carga (`Translator`, arquivos `.tsv` por idioma) |
| Integração com o sistema | Código atrás de `OperatingSystem.Is…()`; atalho global F10 com SharpHook; preferências no registro do Windows ou, no Linux e no macOS, num arquivo JSON |
| Testes | xUnit, NetArchTest (camadas e privacidade), Avalonia.Headless (interface), coverlet; bateria `offline-check.ps1` em execução (Windows, Linux e macOS) |
| Instaladores | Windows: WiX Toolset 6 (MSI e Setup.exe com o Visual C++ Redistributable) e MSIX para a Microsoft Store; Linux: `.deb`; macOS: `.app` e `.dmg` (assinatura e notarização opcionais) |
| CI | GitHub Actions em win-x64, linux-x64, osx-arm64 e osx-x64: compilação, testes, pacotes e testes de instalação |

## Estrutura

```
src/        Desktop (Avalonia), Presentation (view models), Application, Domain, Core, Infrastructure, ModelDownloads e os motores
            (Project, Media, Timeline, Image, Audio, AI, Rendering)
tests/      UnitTests, IntegrationTests, MediaTests
resources/  modelos de exportação, transições, efeitos, LUTs, fontes, catálogo de modelos de IA e traduções
scripts/    dependências, avisos de terceiros, instaladores, extração de textos e bateria offline
installer/  Msi/ (MSI), Bundle/ (Setup.exe) e Store/ (MSIX)
packaging/  macOS (permissões e arquivos do pacote)
documentation/
```

Como compilar, testar e gerar os instaladores: [`INSTALL.pt-BR.md`](INSTALL.pt-BR.md).

## Observações por sistema

- **Linux e macOS são validados só pelo CI** (não há máquina virtual local); avise se encontrar alguma diferença.
- **Linux, Wayland**: o sistema não deixa outros programas capturarem a tela; por isso a gravação de tela fica
  indisponível (com aviso) e o **F10** só funciona com o programa em primeiro plano. Para gravar a tela, escolha uma
  sessão **Xorg** na tela de entrada.
- **macOS, primeira abertura**: se o pacote não foi assinado e notarizado, o Gatekeeper o bloqueia. Libere em
  **Ajustes do Sistema › Privacidade e Segurança › Abrir mesmo assim**. A gravação de tela, a câmera, o microfone e o
  **F10** global pedem a própria permissão na primeira vez que são usados.
- **macOS, Intel**: sem ONNX Runtime (veja [Inteligência artificial](#inteligência-artificial-local)).
- **Linux, IA** roda no processador, e a codificação por VA-API ainda não está ligada.
- **Onde ficam os seus dados** (configurações, projetos recentes, salvamento automático, cache, modelos de IA): no
  Windows, `%APPDATA%\MeuEditorDeVideo` e `%LOCALAPPDATA%\MeuEditorDeVideo`; no Linux, `~/.config/MeuEditorDeVideo` e
  `~/.local/share/MeuEditorDeVideo`; no macOS, `~/Library/Application Support/MeuEditorDeVideo`. Desinstalar não os apaga.

## Perguntas Frequentes (FAQ)

**Quem pode utilizar esse software?**

Todos podem utilizar, desde que tenham as tecnologias necessárias instaladas no computador.

**Quem pode contribuir?**

Qualquer desenvolvedor pode contribuir, seguindo o código de conduta descrito no arquivo
[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).

## Licença

O Meu Editor de Vídeo é software livre, distribuído sob a **GNU General Public License, versão 3 ou posterior
(GPL-3.0-or-later)**. Você pode usar, estudar, modificar e redistribuir o programa nos termos dessa licença. O texto
completo está em [`LICENSE.md`](LICENSE.md). Os componentes de terceiros e as respectivas licenças estão em
[`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).

## Autor

**Diego Mendes Rodrigues** — [LinkedIn](https://www.linkedin.com/in/diegomendesrodrigues/)
