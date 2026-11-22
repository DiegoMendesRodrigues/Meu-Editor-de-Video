# Dependências e licenças

Relação das dependências do **Meu Editor de Vídeo** (ENT018, RNF014, RNF020).
O produto é licenciado sob **GPL-3.0-or-later** (arquivo `LICENSE.md`). Toda dependência deve ser compatível com GPL-3.0.

Versões de pacotes NuGet: `Directory.Packages.props`. Binários nativos: `scripts/deps.lock.json` (com SHA-256), preparados em `deps/` por `pwsh scripts/fetch-deps.ps1` para o sistema em que se roda (Windows, Linux ou macOS; ver "Binários nativos por sistema").

## Pacotes NuGet (produto)

| Pacote | Versão | Licença | Uso |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM (ObservableProperty, RelayCommand) |
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | Injeção de dependências, ciclo de vida |
| Microsoft.Extensions.DependencyInjection.Abstractions / Logging.Abstractions | 10.0.14 | MIT | Contratos de DI e log nas bibliotecas |
| FFmpeg.AutoGen | 9.0.1.1 | LGPL-3.0 | Binding .NET das bibliotecas do FFmpeg (MediaEngine) |
| NAudio.Wasapi (+ NAudio.Core) | 2.2.1 | MIT | Saída de áudio WASAPI da pré-visualização (AudioEngine); referenciado só quando `MedPlataforma` é `windows` |
| ppy.SDL3-CS | 2026.1002.1 | MIT (binding) + zlib (SDL3 nativo) | Saída de áudio SDL3 multiplataforma (AudioEngine): Linux, macOS e, opcionalmente, Windows (`MED_AUDIO_BACKEND=sdl`). Traz o SDL3 nativo para win-x64, linux-x64 e osx-arm64 |
| Serilog.Extensions.Hosting | 10.0.0 | Apache-2.0 | Integração de logs |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Logs técnicos locais rotativos |
| OpenCvSharp4 + OpenCvSharp4.runtime.win | 4.13.0.20260627 | Apache-2.0 (OpenCV: Apache-2.0) | Visão computacional (ImageEngine, F9): rastreamento de movimento (CSRT/correspondência de modelo), estabilização (fluxo óptico), máscaras e "Refinar bordas". Inclui `OpenCvSharpExtern.dll` nativa |
| SkiaSharp + SkiaSharp.HarfBuzz (+ HarfBuzzSharp) | 4.148.0 | MIT | Desenho de textos e formas vetoriais (ImageEngine: `TextRasterizer`, `TextLayout`, `ShapeRasterizer`) em todos os sistemas: diagramação com HarfBuzz (kerning, ligaduras, substituição de fonte por caractere) e contornos com Skia. Substituiu a pilha de texto do WPF |
| Svg.Skia (+ SkiaSharp, HarfBuzzSharp) | 5.2.3 | MIT | Importação e rasterização de gráficos vetoriais SVG (MediaEngine, F9). Inclui `libSkiaSharp.dll` e `libHarfBuzzSharp.dll` nativas |
| Microsoft.ML.OnnxRuntime.DirectML (+ Microsoft.AI.DirectML) | 1.24.4 | MIT | Execução local dos modelos ONNX (AIEngine, F10) na CPU ou na GPU via DirectML, com recuo automático para CPU. Última versão da linha DirectML. Inclui `onnxruntime.dll` e `DirectML.dll` nativas |
| Microsoft.ML.OnnxRuntimeGenAI.DirectML (+ Microsoft.ML.OnnxRuntimeGenAI.Managed, Microsoft.Extensions.AI.Abstractions) | 0.14.1 | MIT | Modelo de linguagem local do assistente virtual (`GenAiAnswerGenerator`): tokenização, geração token a token e cache de atenção, sobre o mesmo `onnxruntime.dll` (exige 1.23 ou mais nova). Inclui `onnxruntime-genai.dll` nativa |
| Whisper.net + Whisper.net.Runtime | 1.9.1 | MIT | Reconhecimento de fala local com whisper.cpp (AIEngine, F10): legendas automáticas. Inclui as bibliotecas nativas do whisper.cpp (CPU) |

### Pacotes por sistema (porte para Linux e macOS)

Os pacotes com bibliotecas nativas diferentes por sistema são escolhidos pela propriedade `MedPlataforma` de `Directory.Build.props`. Ela vale `windows`, `linux` ou `macos`: vem do `RuntimeIdentifier` passado no build (`-r linux-x64`) ou, sem ele, do sistema em que o build roda. Para conferir a restauração de outro sistema, use `-p:MedPlataforma=linux` ou `-p:MedPlataforma=macos`.

| Pacote | Versão | Licença | Sistema | Uso |
|---|---|---|---|---|
| Microsoft.ML.OnnxRuntime.DirectML / Microsoft.ML.OnnxRuntimeGenAI.DirectML | 1.24.4 / 0.14.1 | MIT | windows | Ver acima |
| Microsoft.ML.OnnxRuntime (+ Managed) | 1.24.4 | MIT | linux, macos | Mesmo ONNX Runtime, sem DirectML (CPU; CoreML no macOS). Nativos só para `linux-x64`, `linux-arm64` e `osx-arm64`: **não há `osx-x64`** nesta versão |
| Microsoft.ML.OnnxRuntimeGenAI (+ Managed) | 0.14.1 | MIT | linux, macos | Assistente virtual na CPU. Mesmos sistemas do ONNX Runtime (sem `osx-x64`) |
| OpenCvSharp4.runtime.win | 4.13.0.20260627 | Apache-2.0 | windows | Ver acima |
| OpenCvSharp4.official.runtime.linux-x64 | 4.13.0.20260627 | Apache-2.0 | linux | `libOpenCvSharpExtern.so` para Linux x64 (OpenCV com GTK 3: ver "Dependências do sistema no Linux") |
| OpenCvSharp4.runtime.osx.arm64 / OpenCvSharp4.runtime.osx.x64 | 4.13.0.20260627 | Apache-2.0 | macos | `libOpenCvSharpExtern.dylib` (OpenCV estático, `osx-arm64` e `osx-x64`), publicados pelo próprio OpenCvSharp desde o `4.13.0.20260627`. Os dois são referenciados; o .NET copia só o da RID do build |
| SkiaSharp.NativeAssets.Linux | 4.148.0 | MIT | linux | `libSkiaSharp.so` e `libHarfBuzzSharp.so`. No Windows e no macOS os nativos já vêm pelo Svg.Skia |

Lacuna conhecida: a versão atual do ONNX Runtime não tem Macs Intel (`osx-x64`); no macOS Intel o aplicativo roda sem os recursos de IA (ONNX) até haver um runtime. O OpenCvSharp, antes sem runtime para macOS, passou a ter os pacotes `OpenCvSharp4.runtime.osx.*`, por isso não há compilação própria do `OpenCvSharpExtern`.

## Pacotes NuGet (somente testes)

| Pacote | Versão | Licença |
|---|---|---|
| xunit / xunit.runner.visualstudio | 2.9.3 / 3.1.4 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| coverlet.collector | 6.0.4 | MIT |
| NetArchTest.Rules | 1.3.2 | MIT |

## Binários nativos por sistema

`pwsh scripts/fetch-deps.ps1 [-Strict] [-Force]` é o único ponto de entrada: prepara `deps/` para o sistema em que roda e ignora as entradas do lock que não são dele (campo `platforms`: `windows`, `linux`, `macos`; ausente = todos). Formatos: `.zip`, `.tar.xz`, `.tar.gz`, `.tar.bz2` (o `tar` do sistema; o Windows 11 traz o bsdtar), sempre com SHA-256 conferido. `-Platform linux|macos|windows -DepsDir <pasta>` força outro sistema-alvo em uma pasta temporária (só para testar a lógica; o `kind: "build"` do macOS só roda no próprio macOS).

| Componente | Versão | Licença | Sistema | Origem | Observação |
|---|---|---|---|---|---|
| FFmpeg (full_build, shared) | 9.0.2 | GPL-3.0-or-later | windows | github.com/GyanD/codexffmpeg | Inclui x264, x265, libvpx, SVT-AV1, libaom, LAME, Opus, Vorbis. Exportação (F5): codificadores sondados em tempo de execução (NVENC/AMF/Quick Sync/Media Foundation quando houver GPU, com recuo automático para software). Carregado pelo MediaEngine via FFmpeg.AutoGen. |
| FFmpeg (linux64-gpl-shared) | 9.0.2-61 (`n9.0.2-61-gd59078794d`, autobuild de 21/11/2026) | GPL-3.0-or-later | linux (x64) | github.com/FFmpeg-Builds/FFmpeg-Builds (antes BtbN/FFmpeg-Builds), release imutável `autobuild-2026-11-21-12-37` | Ramo 9.0 (soname `libavcodec.so.63`, o que o FFmpeg.AutoGen 9.0.1.1 espera). Mesmos codecs do Windows, ligados estaticamente. Compilado com glibc antiga: o maior símbolo `GLIBC_` das bibliotecas é 2.28. |
| FFmpeg (compilado das fontes) | 9.0.2 | GPL-3.0-or-later | macos (arm64 e x64) | `scripts/macos/build-ffmpeg.sh` + tarballs `*-src` do lock | Não existe build compartilhado confiável do FFmpeg 9.0 para macOS; compila-se na arquitetura de quem roda (`uname -m`). Fontes: FFmpeg 9.0.2, x264 (ramo stable, commit `0480cb05fa18`, do espelho github.com/mirror/x264: o servidor da VideoLAN recusa downloads automáticos; o conteúdo foi conferido pelo hash do commit), x265 4.3, libvpx 1.17.0, SVT-AV1 4.3.1, libaom 3.15.3, LAME 3.100 (cópia do Debian, idêntica à do SourceForge), Opus 1.6.1, libogg 1.3.6, libvorbis 1.3.7, todas com SHA-256 no lock. Mais VideoToolbox/AudioToolbox do sistema. |
| MODNet (`modnet.onnx`) | 1.0 | Apache-2.0 | todos | huggingface.co/Xenova/modnet | Ver "Modelo de IA incluído no aplicativo". |
| Microsoft Visual C++ Redistributable x64 (`VC_redist.x64.exe`) | 14.51.36247.0 | Licença de redistribuição da Microsoft | windows | download.visualstudio.microsoft.com | Exigido pelas DLLs nativas do ONNX Runtime e do whisper.cpp (MSVCP140, VCRUNTIME140, VCOMP140). Só é usado pelo `Setup.exe` (encadeado antes do MSI, instalado apenas quando falta a versão 14.40 ou mais nova). |

### Contrato de pastas

| Sistema | `deps/` depois do `fetch-deps` | Na saída do build/publicação (`src/AppContent.props`) |
|---|---|---|
| Windows | `ffmpeg/bin/*.dll` e `ffmpeg.exe` (mais `LICENSE`, `doc`…) | `ffmpeg\*.dll`, `ffmpeg\ffmpeg.exe` |
| Linux x64 | `ffmpeg/lib/libav*.so.N`, `libsw*.so.N` (**arquivos reais com o nome do soname**: o script apaga os links `.so` e `.so.N.M.P` e renomeia o arquivo versionado, para o MSBuild não copiar três vezes os 120 MB do `libavcodec`); `ffmpeg/bin/ffmpeg`, `ffprobe`; `ffmpeg/LICENSE.txt` (sem `include`, `doc`, `man`, `presets`, `ffplay`) | `ffmpeg/lib/libav*.so.N` e `ffmpeg/bin/ffmpeg` |
| macOS | `ffmpeg/lib/libav*.N.dylib` (idem, nomes de soname) e `ffmpeg/bin/ffmpeg`, `ffmpeg/LICENSE.txt` | `ffmpeg/lib/*.dylib` e `ffmpeg/bin/ffmpeg` |
| todos | `ai-models/modnet-retrato/modnet.onnx` | `ai-models\modnet-retrato\modnet.onnx` |

O `FFmpegRuntime` procura em `MED_FFMPEG_DIR`, depois em `ffmpeg/` e, no Linux e macOS, `ffmpeg/lib` e `ffmpeg/bin` ao lado do executável (e `deps/ffmpeg/{bin,lib}` no repositório); `FFmpegPlatform.FindCli` acha o `ffmpeg` em `ffmpeg/bin`. O `ffmpeg` de linha de comando da build do Linux tem `RPATH=$ORIGIN/../lib` e o do macOS é compilado com `@executable_path/../lib`, por isso a pasta precisa manter `bin/` e `lib/` irmãs. As bibliotecas `libav*` do Linux não têm RPATH: elas se encontram porque o FFmpeg.AutoGen carrega primeiro `libavutil`/`libswresample` pelo caminho completo. A permissão de execução do `ffmpeg` é preservada pelo `tar` e pela cópia do build em Linux/macOS; ao empacotar em outro sistema (ou em `.deb`/`.tar`), definir 0755 em `ffmpeg/bin/ffmpeg`.

### Linux: glibc mínima

Maior versão de símbolo exigida pelos nativos distribuídos (leitura dos arquivos ELF): FFmpeg 2.28; ONNX Runtime 2.27; ONNX Runtime GenAI 2.28; SkiaSharp 2.27; OpenCvSharpExtern 2.28; whisper.cpp 2.34; SDL3 2.34; libuiohook 2.34. **Mínimo: glibc 2.34**, atendida por Debian 12 (2.36) e Ubuntu 22.04 (2.35). O whisper.cpp também pede `GLIBCXX_3.4.29` (libstdc++ do GCC 11 ou mais novo: Debian 12 e Ubuntu 22.04 trazem o do GCC 12). Debian 11 e Ubuntu 20.04 não são atendidos.

### macOS: como compilar o FFmpeg

`fetch-deps.ps1` baixa as fontes (`kind: "source"`, em `deps/.downloads/sources`) e chama `scripts/macos/build-ffmpeg.sh <fontes> deps/ffmpeg`, que compila cada biblioteca externa estática em uma pasta temporária e depois o FFmpeg compartilhado (`--disable-autodetect`, para não ligar bibliotecas do Homebrew; AVFoundation e as do sistema são pedidas por nome), com `install_name` `@rpath/libX.N.dylib`, `LC_RPATH` `@loader_path` nas bibliotecas e `@executable_path/../lib` no `ffmpeg`, assinatura ad hoc (necessária no Apple Silicon) e verificações finais (nenhuma dependência fora de `@rpath`, `/usr/lib` e `/System`; presença dos codificadores libx264, libx265, libvpx-vp9, libsvtav1, libaom-av1, libmp3lame, libopus, libvorbis e h264_videotoolbox). Pré-requisitos: Xcode Command Line Tools, PowerShell 7 e `brew install cmake pkg-config` (mais `nasm` em Mac Intel). Alvo mínimo do macOS: 11.0. O carimbo `deps/ffmpeg/.version` guarda o hash das fontes e do script; a compilação só se repete quando eles mudam (ou com `-Force`). `MED_KEEP_BUILD=1` mantém a pasta de trabalho para diagnóstico. **O script não foi executado em um Mac por quem o escreveu**: pode precisar de ajustes na primeira execução (pontos mais sensíveis: x265 com 8/10/12 bits unidos, `config.sub` do LAME no arm64 e o alvo `--target` do libvpx).

### Dependências do sistema no Linux

Pacotes que o aplicativo (e o `.deb`, no campo `Depends`) precisa ter instalados. Levantados dos ELF de `-r linux-x64`: `DT_NEEDED` de cada `.so` nativo (FFmpeg, SkiaSharp, HarfBuzzSharp, OpenCvSharpExtern, ONNX Runtime, ONNX Runtime GenAI, whisper.cpp, SDL3, libuiohook). Os nomes com `t64` são os do Ubuntu 24.04 e posteriores (Debian 13), por causa da transição de 64 bits para `time_t`; o Debian 12 e o Ubuntu 22.04 usam o nome antigo.

| Biblioteca (`DT_NEEDED`) | Quem exige | Pacote Debian/Ubuntu |
|---|---|---|
| `libc.so.6`, `libm`, `libdl`, `libpthread`, `librt`, `libutil`, `libmvec` | todos os nativos | `libc6 (>= 2.34)` |
| `libstdc++.so.6` (`GLIBCXX_3.4.29`), `libgcc_s.so.1` | Skia, HarfBuzz, ONNX Runtime, GenAI, OpenCvSharpExtern, whisper | `libstdc++6 (>= 11)`, `libgcc-s1` |
| `libgomp.so.1` | whisper.cpp (ggml) | `libgomp1` |
| `libfontconfig.so.1` | SkiaSharp | `libfontconfig1` |
| `libfreetype.so.6`, `libharfbuzz.so.0` | OpenCvSharpExtern | `libfreetype6`, `libharfbuzz0b` |
| `libgtk-3.so.0`, `libgdk-3.so.0`, `libpangocairo-1.0.so.0`, `libpango-1.0.so.0`, `libatk-1.0.so.0`, `libcairo-gobject.so.2`, `libcairo.so.2`, `libgdk_pixbuf-2.0.so.0`, `libgio-2.0.so.0`, `libgobject-2.0.so.0`, `libglib-2.0.so.0` | OpenCvSharpExtern (o OpenCV oficial é compilado com `highgui` em GTK 3; sem eles a biblioteca não carrega e rastreamento, estabilização e máscaras ficam indisponíveis) | `libgtk-3-0` (`libgtk-3-0t64`); puxa pango, atk, cairo, gdk-pixbuf e glib. Para explicitar: `libglib2.0-0` (`libglib2.0-0t64`), `libcairo2`, `libcairo-gobject2`, `libpango-1.0-0`, `libpangocairo-1.0-0`, `libatk1.0-0` (`libatk1.0-0t64`), `libgdk-pixbuf-2.0-0` |
| `libdrm.so.2`, `libX11.so.6`, `libatomic.so.1` | OpenCvSharpExtern | `libdrm2`, `libx11-6`, `libatomic1` |
| `libasound.so.2`, `libpulse.so.0` ou `libpipewire-0.3.so.0` (carregadas por `dlopen`, não aparecem no ELF) | SDL3 (saída de áudio da pré-visualização; só `SDL_INIT_AUDIO`) | pelo menos um: `libpulse0`, `libasound2` (`libasound2t64`) ou `libpipewire-0.3-0` |
| `libinput.so.10`, `libudev.so.1`, `libwayland-client.so.0`, `libxkbcommon.so.0`, `libXt.so.6`, `libXrandr.so.2`, `libXtst.so.6` | libuiohook (atalho global da gravação de tela, SharpHook; há três variantes: `x11`, `xrecord` e `wayland`, e a que carregar basta) | `Recommends`: `libinput10`, `libudev1`, `libwayland-client0`, `libxkbcommon0`, `libxt6` (`libxt6t64`), `libxrandr2`, `libxtst6` |
| FFmpeg: só glibc e `libgcc_s` (codecs estáticos). Aceleração por VA-API/CUDA/Vulkan vem de `dlopen` das bibliotecas do sistema | FFmpeg | obrigatórias: `libva2`, `libva-drm2`, `libva-x11-2` (ao usar VA-API sem uma delas, o FFmpeg encerra o processo); opcional (`Recommends`): `libvulkan1` |

Lista sugerida para o campo `Depends` do `.deb` (Debian 12 e Ubuntu 22.04; no Ubuntu 24.04 vale a alternativa `t64` de cada nome):

```
libc6 (>= 2.34), libstdc++6 (>= 11), libgcc-s1, libgomp1, libatomic1,
libfontconfig1, libfreetype6, libharfbuzz0b, libx11-6, libdrm2,
libgtk-3-0 | libgtk-3-0t64,
libva2, libva-drm2, libva-x11-2,
libpulse0 | libasound2 | libasound2t64 | libpipewire-0.3-0
```

`Recommends`: `libinput10, libudev1, libwayland-client0, libxkbcommon0, libxt6 | libxt6t64, libxrandr2, libxtst6, libvulkan1`. Se o pacote não for autocontido, o runtime do .NET pede ainda `libicu`, `libssl` e `zlib1g` (ver a documentação do .NET do sistema-alvo). Nenhum `Depends` foi testado em instalação real (não havia Linux disponível); a lista vem da leitura dos arquivos ELF.

## Modelo de IA incluído no aplicativo

| Modelo | Versão | Licença | Origem | Observação |
|---|---|---|---|---|
| MODNet (`modnet.onnx`, 25,9 MB) | 1.0 | Apache-2.0 | huggingface.co/Xenova/modnet, revisão `fa2fa546` (ZHKKKe/MODNet) | Remover fundo, máscaras e rastreamento disponíveis desde a instalação. Baixado por `scripts/fetch-deps.ps1` (SHA-256 no lock) para `deps/ai-models/` e copiado para `ai-models\modnet-retrato\` ao lado do executável, com o manifesto, a licença e a documentação de `resources/ai-models/incluidos/`. |

## Modelos de IA (pacotes `.medmodel`, fora do instalador)

Não fazem parte do repositório nem do instalador (decisão §8 nº 5). O usuário os instala pela ferramenta
**Baixar modelos de IA** (download das URLs oficiais fixadas no catálogo, com SHA-256, só quando clica em Instalar) ou por
pacotes gerados com `tools\Build-ModelPackages.ps1` a partir do catálogo `resources\ai-models\modelos.json`, com o texto da
licença em `resources\ai-models\licencas\`. Detalhes e uso em [MODELOS_IA.md](MODELOS_IA.md).

| Modelo | Origem | Licença | Recurso |
|---|---|---|---|
| Whisper large-v3 turbo (GGML q5_0, 547 MB) | OpenAI Whisper / ggerganov/whisper.cpp | MIT | Legendas automáticas (com Silero VAD, quando instalado, para localizar a fala) |
| GTCRN | Xiaobin Rong (ONNX de streaming: andrelgomes/gtcrn-stream) | MIT | Isolamento de voz e redução de ruído |
| Silero VAD 5 | snakers4/silero-vad | MIT | Detecção de fala / remoção de silêncios |
| TransNetV2 | Tomáš Souček (ONNX: elya5/transnetv2) | MIT | Detecção de cenas |
| Swin2SR leve 2× | mv-lab/swin2sr (ONNX: Xenova/swin2SR-lightweight-x2-64) | Apache-2.0 | Melhoria de imagem |
| Face Mesh V2 | Google MediaPipe (ONNX: fernandotonon/QtMeshEditor-facemesh-onnx) | Apache-2.0 | Retocar: beleza e maquiagem |
| Phi-4-mini-instruct (ONNX int4 para DirectML, 3,2 GB) | Microsoft (microsoft/Phi-4-mini-instruct-onnx) | MIT | Assistente virtual: respostas em linguagem natural |

## Ferramentas de compilação do instalador (não fazem parte do aplicativo)

| Pacote | Versão | Licença | Uso |
|---|---|---|---|
| WixToolset.Sdk | 6.0.2 | MS-RL | Compila o MSI (`installer\Msi`) e o `Setup.exe` (`installer\Bundle`) |
| WixToolset.UI.wixext | 6.0.2 | MS-RL | Telas do instalador (WixUI_Advanced, em pt-BR) |
| WixToolset.Util.wixext / BootstrapperApplications.wixext | 6.0.2 | MS-RL | Detecção do Visual C++ e interface padrão do `Setup.exe` |

> O motor do `Setup.exe` (Burn) e a interface padrão do WiX ficam embutidos no `Setup.exe` (MS-RL). A partir da versão 6, os
> binários do WiX são distribuídos pela FireGiant com o **Open Source Maintenance Fee (OSMF)**: o uso é livre, mas projetos
> que geram receita devem contribuir com a taxa de manutenção. O WiX 7 passou a exigir aceitar o EULA na compilação; por isso
> a versão fica fixada em 6.0.2. Reavaliar se o projeto passar a ter fins comerciais.

## Avisos de terceiros

`THIRD-PARTY-NOTICES.txt` (raiz do repositório, instalado ao lado do executável e aberto por **Ajuda › Licenças de
terceiros**) é gerado por `pwsh scripts/generate-notices.ps1` (no Windows, sem rede) a partir dos pacotes NuGet efetivamente distribuídos
(`project.assets.json` do aplicativo), do FFmpeg de cada sistema (Windows, Linux e as fontes da compilação do macOS, com a oferta de código-fonte da GPL), do MODNet e dos modelos opcionais. Os pacotes NuGet só de Linux e macOS são listados à parte na seção 2. Rodar o script depois de qualquer
atualização de dependência e versionar o resultado.

## Procedimento de atualização

1. Alterar a versão em `Directory.Packages.props` ou `scripts/deps.lock.json` (URL + apagar `sha256`; há uma entrada por sistema em `platforms`).
2. Executar `pwsh scripts/fetch-deps.ps1` em cada sistema afetado (grava o novo hash) e conferir a origem. O FFmpeg do Linux é fixado em um release datado (`autobuild-AAAA-MM-DD-HH-MM`), nunca em `latest`; para trocar, escolher um release do ramo 9.0 com o asset `ffmpeg-n9.0*-linux64-gpl-shared-9.0.tar.xz` e conferir que o soname do `libavcodec` continua `.63`.
3. Rodar todos os testes.
4. Regenerar `THIRD-PARTY-NOTICES.txt` (`pwsh scripts/generate-notices.ps1`).
5. Atualizar esta tabela e registrar no commit o motivo da atualização.
