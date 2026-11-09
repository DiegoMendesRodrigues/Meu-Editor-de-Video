# Dependências e licenças

Relação das dependências do **Meu Editor de Vídeo** (ENT018, RNF014, RNF020).
O produto é licenciado sob **GPL-3.0-or-later** (arquivo `LICENSE`). Toda dependência deve ser compatível com GPL-3.0.

Versões de pacotes NuGet: `Directory.Packages.props`. Binários nativos: `scripts/deps.lock.json` (com SHA-256).

## Pacotes NuGet (produto)

| Pacote | Versão | Licença | Uso |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM (ObservableProperty, RelayCommand) |
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | Injeção de dependências, ciclo de vida |
| Microsoft.Extensions.DependencyInjection.Abstractions / Logging.Abstractions | 10.0.14 | MIT | Contratos de DI e log nas bibliotecas |
| FFmpeg.AutoGen | 9.0.1.1 | LGPL-3.0 | Binding .NET das bibliotecas do FFmpeg (MediaEngine) |
| NAudio.Wasapi (+ NAudio.Core) | 2.2.1 | MIT | Saída de áudio WASAPI da pré-visualização (AudioEngine) |
| Serilog.Extensions.Hosting | 10.0.0 | Apache-2.0 | Integração de logs |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Logs técnicos locais rotativos |
| OpenCvSharp4 + OpenCvSharp4.runtime.win | 4.13.0.20260627 | Apache-2.0 (OpenCV: Apache-2.0) | Visão computacional (ImageEngine, F9): rastreamento de movimento (CSRT/correspondência de modelo), estabilização (fluxo óptico), máscaras e "Refinar bordas". Inclui `OpenCvSharpExtern.dll` nativa |
| Svg.Skia (+ SkiaSharp, HarfBuzzSharp) | 5.2.3 | MIT | Importação e rasterização de gráficos vetoriais SVG (MediaEngine, F9). Inclui `libSkiaSharp.dll` e `libHarfBuzzSharp.dll` nativas |
| Microsoft.ML.OnnxRuntime.DirectML (+ Microsoft.AI.DirectML) | 1.24.4 | MIT | Execução local dos modelos ONNX (AIEngine, F10) na CPU ou na GPU via DirectML, com recuo automático para CPU. Última versão da linha DirectML. Inclui `onnxruntime.dll` e `DirectML.dll` nativas |
| Whisper.net + Whisper.net.Runtime | 1.9.1 | MIT | Reconhecimento de fala local com whisper.cpp (AIEngine, F10): legendas automáticas. Inclui as bibliotecas nativas do whisper.cpp (CPU) |

## Pacotes NuGet (somente testes)

| Pacote | Versão | Licença |
|---|---|---|
| xunit / xunit.runner.visualstudio | 2.9.3 / 3.1.4 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| coverlet.collector | 6.0.4 | MIT |
| NetArchTest.Rules | 1.3.2 | MIT |

## Binários nativos

| Componente | Versão | Licença | Origem | Observação |
|---|---|---|---|---|
| FFmpeg (full_build, shared) | 9.0.2 | GPL-3.0-or-later | github.com/GyanD/codexffmpeg | Inclui x264, x265, libvpx, SVT-AV1, libaom, LAME, Opus, Vorbis. Exportação (F5): codificadores sondados em tempo de execução — NVENC/AMF/Quick Sync/Media Foundation quando houver GPU, com recuo automático para software. SHA-256 no lock. Carregado pelo MediaEngine via FFmpeg.AutoGen; as DLLs são copiadas para `ffmpeg\` ao lado do executável. |

## Modelo de IA incluído no aplicativo

| Modelo | Versão | Licença | Origem | Observação |
|---|---|---|---|---|
| MODNet (`modnet.onnx`, 25,9 MB) | 1.0 | Apache-2.0 | huggingface.co/Xenova/modnet, revisão `fa2fa546` (ZHKKKe/MODNet) | Remover fundo, máscaras e rastreamento disponíveis desde a instalação. Baixado por `scripts/fetch-deps.ps1` (SHA-256 no lock) para `deps/ai-models/` e copiado para `ai-models\modnet-retrato\` ao lado do executável, com o manifesto, a licença e a documentação de `resources/ai-models/incluidos/`. |

## Modelos de IA (pacotes `.medmodel`, fora do instalador)

Não fazem parte do repositório nem do instalador (decisão §8 nº 5). São empacotados com `tools\Build-ModelPackages.ps1`
a partir do catálogo `resources\ai-models\modelos.json`, com o texto da licença em `resources\ai-models\licencas\`.
Detalhes e uso em [MODELOS_IA.md](MODELOS_IA.md).

| Modelo | Origem | Licença | Recurso |
|---|---|---|---|
| Whisper large-v3 turbo (GGML q5_0, 547 MB) | OpenAI Whisper / ggerganov/whisper.cpp | MIT | Legendas automáticas (com Silero VAD, quando instalado, para localizar a fala) |
| GTCRN | Xiaobin Rong (ONNX de streaming: andrelgomes/gtcrn-stream) | MIT | Isolamento de voz e redução de ruído |
| Silero VAD 5 | snakers4/silero-vad | MIT | Detecção de fala / remoção de silêncios |
| TransNetV2 | Tomáš Souček (ONNX: elya5/transnetv2) | MIT | Detecção de cenas |
| Swin2SR leve 2× | mv-lab/swin2sr (ONNX: Xenova/swin2SR-lightweight-x2-64) | Apache-2.0 | Melhoria de imagem |

## Planejadas (ainda não adicionadas)

| Componente | Versão candidata | Licença | Fase |
|---|---|---|---|
| Vortice.Direct3D11 / DXGI / D2D / DirectWrite | 3.8.x | MIT | F4 |
| NAudio | 3.3.0 | MIT | F4/F7 |
| WiX Toolset | — | MS-RL | F12 |

## Procedimento de atualização

1. Alterar a versão em `Directory.Packages.props` ou `scripts/deps.lock.json` (URL + apagar `sha256`).
2. Executar `pwsh scripts/fetch-deps.ps1` (grava o novo hash) e conferir a origem.
3. Rodar todos os testes.
4. Atualizar esta tabela e registrar no commit o motivo da atualização.
