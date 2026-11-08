# Plano de Implantação — Meu Editor de Vídeo

**Base:** `ESPECIFICACAO_TECNICA_E_FUNCIONAL.md` v1.0 (215 RF, 20 RNF, 24 CA, 18 entregáveis) e protótipos em `Imagens/`.
**Data:** 08/11/2026
**Ambiente de desenvolvimento verificado:** Windows 11 Pro, .NET SDK 10.0.403, Git 2.56, NVIDIA RTX 3070 (8 GB). FFmpeg **não** instalado.

---

## 1. Leitura da especificação — o que ela realmente pede

A especificação descreve um editor não linear (NLE) completo, comparável em escopo a produtos comerciais maduros, com quatro motores pesados (mídia, composição/render, áudio, IA) e uma interface WPF própria. Três diretrizes da seção 17 moldam todo o plano:

1. **Versão única com todas as funcionalidades** — as fases abaixo são *marcos internos de engenharia*, não versões públicas. Nada é entregue ao usuário final antes do CA001–CA024.
2. **Nada simulado apresentado como pronto** — cada recurso só aparece na UI quando funciona de ponta a ponta (inclusive IA sem modelo instalado: deve aparecer como "indisponível", §8.3).
3. **100% local** — nenhuma chamada de rede em tempo de execução, exceto a que o usuário iniciar explicitamente (nenhuma está prevista).

### 1.1. Lacunas e inconsistências encontradas

| # | Ponto | Encaminhamento proposto |
|---|---|---|
| L1 | Exemplo JSON do §9.4 está malformado (crases dentro do JSON, `sequence` fora do objeto). | Substituir por um JSON Schema formal (`med-schema-1.0.json`) na Fase 1. |
| L2 | "Elementos 3D por meio de recursos de composição compatíveis" (RF154) é vago. | Definir como **camadas 2,5D**: transformação 3D com perspectiva (rotação X/Y/Z, profundidade, câmera virtual) no compositor Direct3D. Importação de modelos 3D (glTF) fica fora, salvo decisão contrária. |
| L3 | "Perfil de cores" (§5.2) sem lista de valores. | Rec.709 (padrão), sRGB, Rec.2020 SDR. HDR (PQ/HLG) fora do escopo v1.0. |
| L4 | Formatos de exportação sem codecs associados. | Ver tabela §4.4. |
| L5 | Protótipos usam logotipos de YouTube, Instagram, TikTok, WhatsApp, Facebook. | **Decidido:** ícones genéricos (formato/proporção, ex.: retângulo 16:9, celular 9:16, quadrado 1:1) com o nome do destino em texto. |
| L6 | Protótipos divergem entre si (menu lateral com "Filtros"/"IA" num, "Elementos Gráficos"/"Projetos" no outro; o botão Exportar abre "Exportar Vídeo" num e "Biblioteca de Exportação" no outro). | Consolidar no §3 abaixo (uma única tela de exportação, que mescla os dois). |
| L7 | "Retomada de renderização quando tecnicamente suportada". | Renderização segmentada (blocos de N segundos, concatenados ao final) — permite retomar a partir do último bloco válido. |
| L8 | RF019 "abrir projetos independentes em outros computadores" + mídias dentro do ZIP. | Mídias armazenadas sem compressão (`Stored`) e extraídas para o cache local na abertura (decodificação com acesso aleatório dentro do ZIP é inviável). |
| L9 | Requisitos de hardware citam GPU de 4 GB, mas RNF009 exige funcionar sem GPU dedicada. | Pipeline duplo: Direct3D 11 (inclusive WARP/iGPU) e fallback CPU para efeitos; IA em CPU com aviso de desempenho. |

---

## 2. Arquitetura técnica

### 2.1. Pilha de tecnologias (tecnologias obrigatórias + bibliotecas escolhidas)

| Área | Escolha | Licença | Observação |
|---|---|---|---|
| Linguagem / runtime | C# 14, .NET 10 LTS | MIT | Já instalado (10.0.403). |
| UI | WPF + **tema Fluent nativo** (`ThemeMode` Light/Dark/System) | MIT | Atende RF207 sem biblioteca de terceiros. |
| MVVM | CommunityToolkit.Mvvm | MIT | Source generators para `ObservableProperty`/`RelayCommand`. |
| DI / host | Microsoft.Extensions.Hosting | MIT | DI, configuração e ciclo de vida. |
| Logs | Serilog (arquivo rotativo em `%LOCALAPPDATA%`) | Apache 2.0 | Sem conteúdo audiovisual nos logs (§13). |
| Decodificação/codificação | **FFmpeg 7.x, build GPL v3, shared** via FFmpeg.AutoGen | GPL 3 / MIT | Produto licenciado como **GPL-3.0-or-later**, o que permite `--enable-gpl --enable-version3`. |
| Codificação H.264/HEVC por software | **x264 / x265** (via FFmpeg) | GPL 2+ | Melhor qualidade por bitrate quando não há GPU. Media Foundation fica como alternativa. |
| Hardware encode/decode | NVENC/NVDEC, AMF, Quick Sync, D3D11VA (via FFmpeg) | — | Seleção automática + manual (RF189, RF211). |
| Composição / efeitos | **Direct3D 11** via Vortice.Windows, shaders HLSL | MIT | Transformações, transições, cor, LUT, chroma key, máscaras. |
| Exibição na WPF | Superfície D3D11 compartilhada → `D3DImage` (D3D9Ex) | — | Fallback `WriteableBitmap` para WARP/CPU. |
| Visão computacional | OpenCvSharp4 (OpenCV 4.x) | Apache 2.0 | Estabilização, rastreamento, detecção de cenas, refinamento de bordas. |
| Texto | DirectWrite/Direct2D via Vortice | MIT | Títulos, legendas, contornos e sombras renderizados na GPU. |
| Vetores | Svg.Skia (SVG → textura) | MIT | RF152. |
| Áudio (reprodução/mixagem) | NAudio (WASAPI) + DSP próprio | MIT | Relógio de áudio como relógio mestre da reprodução. |
| Áudio (processamento) | Filtros FFmpeg (`loudnorm`, `acompressor`, `equalizer`, `silencedetect`, `afftdn`) + modelos ONNX | LGPL | Normalização EBU R128 (RF126). |
| IA | ONNX Runtime + **DirectML** (NVIDIA/AMD/Intel) e CUDA opcional | MIT | RF167: CPU / GPU escolhíveis. |
| Fala | Whisper.net (whisper.cpp) com modelos ggml | MIT | "Whisper local" exigido pelo §2.1; §8.1 permite outro runtime local. |
| Testes | xUnit, FluentAssertions\*, Verify (snapshots de JSON e de quadros) | Apache/MIT | \*Verificar licença vigente da versão escolhida. |
| Instalador | **WiX Toolset** (MSI) | MS-RL | "Windows Installer"; associação `.med`, ícone, desinstalação limpa. |

> Todas as versões serão fixadas em `Directory.Packages.props` (Central Package Management) e o binário FFmpeg terá hash registrado em `scripts/deps.lock.json` (RNF014).

### 2.2. Modelos de IA candidatos (compatíveis com GPL-3.0 — RNF020)

Com o produto em GPL-3.0, modelos GPL-3.0 também passam a ser aceitáveis — em especial **RobustVideoMatting** (GPL-3.0), que tem consistência temporal superior para vídeo e passa a ser o candidato principal de remoção de fundo, com MODNet/BiRefNet como alternativa leve. Modelos AGPL (ex.: YOLOv8+) continuam evitados para não impor obrigações adicionais a quem redistribuir. Cada modelo é distribuído em **pacote `.medmodel` separado** do instalador (decisão §8).

| Função | Modelo candidato | Licença | Alternativa |
|---|---|---|---|
| Segmentação de pessoas / remoção de fundo (RF156, RF162) | MODNet ou BiRefNet (ONNX) | Apache 2.0 / MIT | MediaPipe Selfie Segmentation (Apache 2.0). *Evitar RobustVideoMatting (GPL-3.0).* |
| Reconhecimento de fala / legendas (RF157, RF158) | Whisper (small/medium, ggml) | MIT | — |
| Isolamento de voz (RF159, RF127) | Demucs/HT-Demucs exportado para ONNX | MIT | — |
| Redução de ruído (RF160, RF124) | DeepFilterNet (ONNX) | MIT/Apache 2.0 | `afftdn` (FFmpeg) como modo não-IA. |
| Detecção de cenas (RF161) | TransNetV2 (ONNX) | MIT | Diferença de histogramas OpenCV (sem modelo). |
| Rastreamento de objetos (RF163, RF150) | OpenCV Tracker Nano/ViT + CSRT | Apache 2.0 | *Evitar YOLOv8+ (AGPL).* |
| Melhoria de imagem (RF164) | Real-ESRGAN (ONNX) | BSD-3 | — |
| Silêncios (RF165) | Silero VAD (ONNX) | MIT | `silencedetect` (FFmpeg). |

Pacote de modelo: arquivo `.medmodel` (ZIP) com `model.json` contendo todos os campos do §8.3, verificação SHA-256 na instalação e a cada carregamento.

### 2.3. Organização da solução (segue o §11)

```
MeuEditorDeVideo/
├── MeuEditorDeVideo.sln
├── Directory.Build.props / Directory.Packages.props
├── src/
│   ├── MeuEditorDeVideo.App              (WPF: Views, ViewModels, controles, temas, recursos pt-BR)
│   ├── MeuEditorDeVideo.Application      (casos de uso, comandos, fila de tarefas, orquestração)
│   ├── MeuEditorDeVideo.Domain           (entidades, regras, tempo racional, comandos undo/redo)
│   ├── MeuEditorDeVideo.Core             (abstrações compartilhadas, Result, erros pt-BR, interfaces de motores)
│   ├── MeuEditorDeVideo.Infrastructure   (arquivos, cache, logs, configurações, detecção de hardware)
│   ├── MeuEditorDeVideo.ProjectEngine    (.med: ZIP, schema, migrações, salvamento atômico, autosave)
│   ├── MeuEditorDeVideo.MediaEngine      (FFmpeg: probe, decodificação, miniaturas, formas de onda, proxies)
│   ├── MeuEditorDeVideo.TimelineEngine   (avaliação da timeline num instante → grafo de composição)
│   ├── MeuEditorDeVideo.ImageEngine      (Direct3D 11, shaders, cor, LUT, chroma, máscaras, texto, OpenCV)
│   ├── MeuEditorDeVideo.AudioEngine      (mixagem, DSP, reprodução WASAPI, sincronização)
│   ├── MeuEditorDeVideo.RenderingEngine  (pipeline de exportação, encoders, fila, segmentação)
│   └── MeuEditorDeVideo.AIEngine         (gerenciador de modelos, ONNX Runtime, Whisper)
├── tests/ (UnitTests, IntegrationTests, MediaTests)
├── resources/ (templates de exportação JSON, transições, efeitos, LUTs, fontes, ai-models)
├── installer/ (WiX)
├── documentation/
└── scripts/ (build, obtenção e verificação de dependências)
```

**Regra de dependências:** `Domain` não referencia nada; `Core` ← `Domain`; motores dependem de `Core`/`Domain`; `Application` depende das interfaces dos motores; `App` depende só de `Application` (+ composição DI no `App`). Validado automaticamente por teste de arquitetura (NetArchTest).

### 2.4. Decisões técnicas centrais

- **Tempo (§6.4):** todo tempo interno em `long` na base de **flicks** (705.600.000 por segundo). Ela divide exatamente 23,976 (24000/1001), 24, 25, 29,97 (30000/1001), 30, 50, 59,94 e 60 FPS e as taxas de áudio usuais (44,1 kHz e 48 kHz). FPS armazenado como fração (`fpsNumerator`/`fpsDenominator`). Nenhum `double` em posições da timeline.
- **Edição não destrutiva (§6.1):** clipes referenciam `MediaAsset` por ID + intervalos de entrada/saída; os arquivos de origem nunca são abertos para escrita.
- **Desfazer/refazer (RF012–RF014):** padrão *Command* no domínio (`IEditCommand` com `Do`/`Undo`), composição de comandos para operações compostas (ripple, agrupamento), histórico visível e persistido no autosave.
- **Pré-visualização × renderização (§6.5):** os dois usam o mesmo `TimelineEvaluator` (gera um grafo de composição para o instante *t*) e os mesmos shaders, mas pipelines de execução diferentes: a pré-visualização usa proxies, resolução reduzida, cache de quadros e pode descartar quadros; a renderização é determinística, quadro a quadro, em resolução total. Isso garante que "o que se vê é o que se exporta".
- **Assincronismo (RNF006/007):** `Channel<T>` + `Task` com `CancellationToken` em todos os motores; um `TaskManager` central na camada de aplicação alimenta o painel de tarefas (progresso, velocidade, tempo estimado, cancelamento).
- **Erros (§7.4, RNF010):** exceções técnicas convertidas em `MedError` com código + mensagem em pt-BR (catálogo em `.resx`), registradas no log sem conteúdo do usuário.
- **Privacidade (RF169, CA016):** nenhum `HttpClient` registrado no DI; teste automatizado que falha se algum assembly da solução referenciar `System.Net.Http` fora de uma lista de exceções vazia; teste de execução com rede desabilitada.

---

## 3. Interface (consolidação dos protótipos)

Os protótipos estão coerentes com o §5 e serão a referência visual: tema escuro padrão, azul de destaque (~#1E7BFF), barra de título própria com ícone.

| Tela | Conteúdo (spec + protótipo) |
|---|---|
| **Tela inicial** (§5.1 — não prototipada) | Logotipo, Novo Projeto, Abrir Projeto, recentes com miniatura, "Recuperar projeto" (aparece quando houver autosave órfão), Configurações, versão. |
| **Novo projeto** (§5.2 — não prototipada) | Nome, diretório, modelo (presets: 1080p 16:9, 4K, vertical 9:16, quadrado 1:1…), resolução, proporção, FPS, perfil de cores, modo (vinculado/independente). |
| **Editor** | Menu (Arquivo, Editar, Visualizar, Projeto, Ferramentas, Ajuda) + barra de ações (Novo, Abrir, Salvar, Desfazer, Refazer, Importar Mídia, Exportar, tema). **A** navegação lateral (Biblioteca, Vídeos, Imagens, Áudios, Elementos Gráficos, Textos, Transições, Efeitos, Filtros/Cor, IA) + grade de mídias com busca; **B** monitor com controles, volume, tempo `hh:mm:ss:ff`, tela cheia, captura de quadro; **C** propriedades em abas (Vídeo, Áudio, Efeitos, Cor) com keyframe por propriedade; **E** barra de ferramentas sobre a timeline; **D** timeline com cabeçalho de trilha (visível, mudo, bloqueio), régua, playhead, zoom, miniaturas em clipes de vídeo, forma de onda em áudio, cores por tipo (vídeo azul, imagem verde, áudio verde/roxo, texto roxo). |
| **Exportação** | Mescla dos dois protótipos: lista dos 12 modelos à esquerda; parâmetros (formato, resolução, proporção, FPS, codec, qualidade, bitrate, áudio) ao centro; prévia à direita; destino + nome; "Renderizar em segundo plano"; Renderizar; "Restaurar padrões", "Duplicar", "Salvar como novo modelo". Painel de fila com progresso, velocidade (fps/×tempo real), tempo estimado e erros. |
| **Configurações** | Geral (idioma, tema, iniciar com último projeto), Vídeo (qualidade da pré-visualização, proxies), Áudio (dispositivo, taxa), Desempenho (GPU, cache e tamanho, autosave e intervalo, renderização em segundo plano), Diretórios padrão, Atalhos (editáveis, com detecção de conflito), IA Local (modelos instalados, CPU/GPU, memória exigida, instalar pacote), Sobre (versão, informações do sistema, licenças de terceiros). |

O controle de **timeline** será um controle personalizado desenhado com `DrawingVisual`/`OnRender` e virtualização horizontal (somente clipes visíveis), não um `ItemsControl` com milhares de elementos — é a parte da UI com maior risco de desempenho.

---

## 4. Especificações complementares

### 4.1. Formato `.med`
- ZIP com `manifest.json` (id, nome, `formatVersion`, datas, modalidade `linked|packaged`, lista de arquivos, tamanhos, SHA-256) + `project.json` + pastas do §9.1.
- `project.json` validado por JSON Schema versionado; cadeia de migradores `IMedMigration (1.0 → 1.1 → …)` aplicada na abertura, com cópia de segurança antes de migrar (RF015, RF020, §9.5).
- **Salvamento atômico:** grava `nome.med.tmp` no mesmo volume → `Flush(true)` → `File.Replace` com `.bak`. Falha em qualquer etapa preserva o arquivo anterior (RNF015).
- **Autosave/recuperação:** a cada N minutos (configurável, RF213) grava em `%LOCALAPPDATA%\MeuEditorDeVideo\autosave\{projectId}\`; arquivo de "sessão aberta" detecta encerramento inesperado e oferece recuperação na próxima inicialização (RF010, RF011).
- Mídias ausentes: verificação na abertura por caminho absoluto → relativo ao `.med` → busca por nome+tamanho+hash parcial em pasta indicada pelo usuário (RF016, RF017).
- Empacotar (RF018): copia mídias para `media/` (sem compressão), reescreve referências, troca a modalidade para `packaged`.

### 4.2. Importação (RF021–RF036)
Vídeo (MP4, MOV, MKV, AVI, WebM, MPEG, M4V, WMV), imagem (JPG, PNG, BMP, TIFF, WebP, SVG), áudio (MP3, WAV, AAC, M4A, FLAC, OGG, Opus), GIF animado, sequência de imagens (padrão `nome_####.png` detectado automaticamente). Probe com FFmpeg em segundo plano, miniaturas e formas de onda em cache, proxies opcionais (H.264 540p, gerados sob demanda/automaticamente acima de 1080p).

### 4.3. Renderização (§7)
Pipeline: validar projeto → construir grafo → para cada quadro: decodificar (hardware se disponível) → transformar/efeitos/compor na GPU → readback ou superfície compartilhada com o encoder → codificar; áudio mixado em paralelo em blocos → multiplexar → validar o arquivo final (probe + duração esperada ± 1 quadro). Renderização de intervalo, segmentada e retomável; fila persistida em disco.

### 4.4. Formatos de exportação (RF171–RF177)

| Contêiner | Vídeo | Áudio | Alfa |
|---|---|---|---|
| MP4 | H.264, HEVC (NVENC/AMF/QSV/MF), AV1 (hardware ou SVT-AV1) | AAC | — |
| MOV | H.264, HEVC, ProRes (`prores_ks`) | AAC, PCM | ProRes 4444, QuickTime Animation |
| MKV | H.264, HEVC, AV1, VP9 | AAC, Opus, FLAC | FFV1 |
| WebM | VP9 (libvpx), AV1 | Opus, Vorbis | VP9 com alfa |
| AVI | MJPEG, H.264 | PCM, MP3* | — |
| MPEG | MPEG-2 (`mpeg2video`) | MP2 | — |
| GIF | GIF (`palettegen`/`paletteuse`) | — | Transparência binária |

\*MP3 via LAME (LGPL) — compatível com GPL-3.0. Em MP4/MOV/MKV, H.264/HEVC por software usam x264/x265.

### 4.5. Biblioteca de modelos de exportação (§10)
Arquivos JSON em `resources/templates/export/EXP001.json…EXP012.json` (somente leitura, restauráveis) + modelos do usuário em `%APPDATA%\MeuEditorDeVideo\templates\`. Valores iniciais propostos:

| ID | Modelo | Resolução | FPS | Vídeo | Áudio |
|---|---|---|---|---|---|
| EXP001 | YouTube | 1920×1080 16:9 | 30 (ou do projeto) | H.264 High, 8 Mbps VBR | AAC 192 kbps |
| EXP002 | YouTube Shorts | 1080×1920 9:16 | 30 | H.264, 8 Mbps | AAC 192 kbps |
| EXP003 | Instagram Feed | 1080×1350 4:5 | 30 | H.264, 6 Mbps | AAC 128 kbps |
| EXP004 | Instagram Reels | 1080×1920 9:16 | 30 | H.264, 6 Mbps | AAC 128 kbps |
| EXP005 | TikTok | 1080×1920 9:16 | 30 | H.264, 6 Mbps | AAC 128 kbps |
| EXP006 | WhatsApp Status | 720×1280 9:16 | 30 | H.264 Main, 2 Mbps, ≤ 60 s por arquivo* | AAC 128 kbps |
| EXP007 | WhatsApp compartilhamento | 1280×720 16:9 | 30 | H.264 Main, 2,5 Mbps, alvo de tamanho* | AAC 128 kbps |
| EXP008 | Facebook Feed | 1080×1080 1:1 | 30 | H.264, 6 Mbps | AAC 128 kbps |
| EXP009 | Facebook Reels | 1080×1920 9:16 | 30 | H.264, 6 Mbps | AAC 128 kbps |
| EXP010 | Apresentações | 1920×1080 16:9 | 30 | H.264, 10 Mbps, compatibilidade máxima | AAC 192 kbps |
| EXP011 | Computadores e notebooks | 1920×1080 16:9 | do projeto | H.264/HEVC qualidade constante | AAC 256 kbps |
| EXP012 | Exportação personalizada | livre | livre | livre | livre |

\*Limites das plataformas mudam com frequência; os valores ficam nos JSON exatamente para serem revisados sem alterar código (§10). Validar antes da entrega.

---

## 5. Fases de implantação

Cada fase termina com: código revisado, testes verdes no CI, funcionalidades visíveis na UI **funcionando de verdade** (as demais ocultas), e matriz de rastreabilidade RF→teste atualizada.

| Fase | Entrega | RF cobertos | Duração estimada* |
|---|---|---|---|
| **F0 — Fundação** | Solução e projetos do §11; CPM; CI (build + testes); script que baixa/verifica FFmpeg LGPL e OpenCV por hash; DI, Serilog, tratamento global de exceções; recursos pt-BR; tema Fluent claro/escuro; casca do editor com os 5 painéis vazios; tela inicial; ícone convertido para `.ico` multi-resolução (16–256 px). | RF206, RF207, RF215 (parcial) | 4 sem |
| **F1 — Domínio e projeto .med** | Entidades do §2.3.3; tempo em flicks; comandos com undo/redo e histórico; `.med` com schema, migrações, salvamento atômico, autosave, recuperação, backups, empacotamento; telas Novo Projeto/Abrir/Salvar como. | RF001–RF020 | 6 sem |
| **F2 — Media Engine** | Probe, importação múltipla e arrastar-soltar, sequências de imagens, GIF, miniaturas, formas de onda, proxies, categorias, busca, painel de informações, detecção de corrompidos, relink. | RF021–RF036 | 6 sem |
| **F3 — Timeline** | Controle de timeline personalizado; 6 tipos de trilha; adicionar/mover/copiar/duplicar/remover/cortar/aparar/dividir; ripple; snap; grupos; bloqueio/ocultar/mudo; marcadores; zoom; navegação por quadros; velocidade de clipe. | RF037–RF064 | 8 sem |
| **F4 — Pré-visualização** | Decodificação com hardware; compositor D3D11 multitrilha; exibição via `D3DImage`; reprodução de áudio WASAPI com relógio mestre; cache de quadros; resolução reduzida; tela cheia; captura de quadro. **Marco: fatia vertical** (importar → editar → ver e ouvir). | §6.5, RF058, RF059, CA006 | 8 sem |
| **F5 — Renderização e exportação** | Render Engine completo (§7.2); todos os contêineres/codecs do §4.4; encoders NVENC/AMF/QSV/MF/software; 12 modelos; modelos do usuário; fila em segundo plano; progresso, velocidade, ETA, cancelamento, intervalo, segmentação/retomada; alfa; mensagens de erro pt-BR. | RF171–RF205 | 8 sem |
| **F6 — Edição visual, efeitos e cor** | Recorte, rotação, espelhamento, escala, opacidade, câmera lenta (com interpolação de quadros opcional), aceleração, congelar, repetir; keyframes com curvas de easing; modos de mistura; transições (corte, fade, dissolve, zoom, laterais, geométricas); biblioteca de efeitos combináveis; cor (brilho…balanço de branco, curvas, rodas, LUT `.cube`), comparação antes/depois; chroma key verde/azul/cor livre com tolerância, suavização, despill. | RF065–RF074, RF076–RF101, RF112–RF118, RF144–RF148, RF151 | 12 sem |
| **F7 — Áudio** | Corte/divisão, volume com keyframes, fades, EQ paramétrico, compressor, normalização EBU R128, redução de ruído (DSP), remoção de silêncio, sincronização por correlação cruzada (RF059/RF129), mixer com medidores, efeitos sonoros. | RF119–RF131 (exceto partes de IA) | 6 sem |
| **F8 — Textos e legendas** | Textos, títulos, créditos rolantes, tarjas (lower thirds) com DirectWrite; fontes do sistema + embarcadas; contorno, sombra; animações de texto; importação SRT/VTT; editor de legendas; exportação de legendas queimadas e em arquivo. | RF132–RF141, RF143 | 6 sem |
| **F9 — Composição avançada (OpenCV)** | Estabilização; máscaras manuais (Bézier) e animadas; rastreamento de movimento e vínculo de elementos ao movimento; elementos vetoriais SVG; animações 2D; camadas 2,5D; refinamento de bordas. | RF075, RF110, RF111, RF149, RF150, RF152–RF154 | 8 sem |
| **F10 — IA local** | Gerenciador de modelos (pacotes `.medmodel`, hash, licença, memória, CPU/GPU, compatibilidade); remoção/substituição de fundo (cor, imagem, vídeo, desfoque); Whisper → legendas; isolamento de voz; redução de ruído IA; detecção de cenas → marcadores; segmentação; rastreamento inteligente; melhoria de imagem; detecção de silêncio. Processamento em lote em segundo plano com cache de máscaras. | RF102–RF109, RF127, RF142, RF155–RF170 | 12 sem |
| **F11 — Configurações e desempenho** | Telas de configurações completas; atalhos editáveis; detecção de hardware (§12.3) e ajuste automático da qualidade de pré-visualização; limites de memória/cache; perfilamento com 4K multitrilha. | RF208–RF214, RNF008/009/013 | 4 sem |
| **F12 — Qualidade, instalador e documentação** | Testes de integração, audiovisuais (§14.3), IA (§14.4) e de recuperação (§14.5); bateria offline; MSI WiX com associação `.med`, atalhos e desinstalação limpa; assinatura de código; documentação técnica, manual do usuário, relação de dependências e licenças (`THIRD-PARTY-NOTICES`); homologação CA001–CA024. | RNF017–RNF020, CA001–CA024, ENT001–ENT018 | 8 sem |

\*Durações estimadas para uma equipe humana de ~5 pessoas, mantidas apenas como medida relativa de tamanho entre fases. **Decisão §8: o desenvolvimento será feito somente pelo Claude Code** (ver §10); o ritmo real dependerá principalmente da velocidade das validações manuais feitas pelo responsável do produto.

### 5.1. Caminho crítico
F0 → F1 → F2 → F3 → **F4 (fatia vertical)** → F5. Depois de F5 existe um editor básico funcional de ponta a ponta; F6–F10 adicionam recursos sobre o mesmo pipeline. Por isso o compositor D3D11 e o `TimelineEvaluator` precisam nascer na F4 já extensíveis (grafo de nós), para que efeitos, máscaras e IA entrem como novos nós, não como reescrita.

---

## 6. Estratégia de testes (§14, RNF019)

- **Unitários:** tempo/conversões (todas as taxas do §6.4, sem desvio após 24 h de timeline), operações de timeline, undo/redo (propriedade: `Do`+`Undo` = estado original), serialização `.med` (ida e volta, migrações), modelos de exportação.
- **Integração:** cada motor contra a dependência real (FFmpeg, ONNX Runtime, Direct3D WARP no CI).
- **Audiovisuais:** acervo de mídias de teste geradas por script (`ffmpeg -f lavfi testsrc2/sine`), garantindo licença livre: HD/FHD/4K, vertical, VFR, 23,976/29,97/59,94, mono/estéreo/5.1, alfa (ProRes 4444/VP9). Comparação de quadros renderizados por SSIM/PSNR com referência.
- **IA:** precisão mínima em conjunto de teste próprio (ex.: WER do Whisper em áudio pt-BR, IoU das máscaras), execução em CPU e GPU, e **com rede desabilitada**.
- **Recuperação:** kill do processo durante salvamento/renderização/IA, disco cheio (volume VHD pequeno), mídias removidas.
- **Arquitetura e privacidade:** testes de dependências entre camadas e ausência de uso de rede.

---

## 7. Riscos principais

| Risco | Impacto | Mitigação |
|---|---|---|
| Desempenho de pré-visualização 4K multitrilha | Alto | Proxies, decodificação por hardware, superfícies compartilhadas sem cópia CPU, cache de quadros, protótipo técnico na F0/F4. |
| Interop D3D11 ↔ WPF (`D3DImage` exige D3D9Ex) | Alto | Spike técnico na primeira semana da F4; fallback `WriteableBitmap`. |
| Licenciamento (GPL-3.0, patentes H.264/HEVC/AAC, modelos de IA) | Alto | Código-fonte publicado junto com cada binário (obrigação da GPL); `THIRD-PARTY-NOTICES` gerado por script; patentes de codecs avaliadas conforme o país de distribuição; revisão jurídica antes da F12. |
| Qualidade da IA em vídeo (oscilação de máscara entre quadros) | Médio | Suavização temporal das máscaras, refinamento com guided filter (OpenCV). |
| Tamanho do instalador (FFmpeg + OpenCV + ONNX + modelos podem passar de 3 GB) | Médio | Instalador base sem modelos grandes + pacotes `.medmodel` locais opcionais (RF170 já prevê isso). |
| Escopo (215 RF numa versão única) | Alto | Fatia vertical cedo, rastreabilidade RF→teste, revisão de escopo a cada fase. |
| Variedade de GPUs/drivers | Médio | Matriz de testes NVIDIA/AMD/Intel/sem GPU; detecção de falha de GPU com retorno automático para CPU (§7.4). |

---

## 8. Decisões tomadas (08/11/2026)

| # | Tema | Decisão |
|---|---|---|
| 1 | Licença do produto | **GPL** (proposta: GPL-3.0-or-later). Libera x264/x265, FFmpeg GPL e modelos GPL. Exige publicar o código-fonte junto com cada distribuição. |
| 2 | Equipe | **Somente o Claude Code** (§10). |
| 3 | RF154 (3D) | **Camadas 2,5D** atendem. |
| 4 | Modelos de exportação | **Ícones genéricos**, sem logotipos de terceiros. |
| 5 | Modelos de IA | **Pacotes `.medmodel` separados** do instalador. |
| 6 | Assinatura de código | **Sem certificado** no momento — encaminhamento em §11. |
| 7 | Telemetria/atualização automática | Nenhuma (RNF003/RNF005). |

---

## 9. Próximos passos imediatos

1. ~~Validar este plano e responder às decisões do §8.~~ Concluído.
2. `git init`, criar a estrutura da solução (§2.3) e a F0.
3. Spikes técnicos de 1–2 semanas, antes de comprometer a arquitetura: (a) decodificar 4K com NVDEC e exibir via D3D11→`D3DImage` a 60 fps; (b) codificar H.264 via Media Foundation e via NVENC; (c) rodar MODNet via ONNX Runtime/DirectML na RTX 3070.

---

## 10. Modelo de trabalho com o Claude Code

**Divisão de modelos** (configurada em `.claude/settings.json`: sessão principal `opus`, `CLAUDE_CODE_SUBAGENT_MODEL=haiku`):

| Papel | Modelo | Responsabilidades |
|---|---|---|
| Consultor / arquiteto / revisor | **Opus** (sessão principal) | Decompor cada fase em tarefas pequenas com critérios de aceitação e testes; escrever contratos (interfaces, schemas, ADRs); implementar pessoalmente as partes críticas (modelo de tempo, interop D3D11↔WPF, ponte FFmpeg, grafo de composição, salvamento atômico); revisar todo código produzido pelos subagentes antes do commit. |
| Implementadores | **Haiku** (subagentes) | Tarefas bem delimitadas: ViewModels e Views a partir de contrato, comandos de timeline, shaders a partir de fórmula especificada, parsers SRT/VTT, templates JSON, testes unitários, recursos pt-BR, documentação. |

**Ciclo por tarefa:** Opus especifica (arquivo-alvo, interface, testes que devem passar) → Haiku implementa → `dotnet build` + `dotnet test` como portão obrigatório → Opus revisa o diff → commit. Tarefas que falharem duas vezes no Haiku voltam para o Opus.

**O que continua dependendo de você (não automatizável pelo Claude Code):**
- Executar o aplicativo e validar visualmente/auditivamente pré-visualização, cores, sincronia e qualidade da exportação (o Claude Code pode gerar testes objetivos — SSIM, duração, probe — mas não "assistir" ao vídeo em tempo real).
- Testes em outras máquinas/GPUs (AMD, Intel, sem GPU dedicada).
- Aprovar decisões de produto, revisão jurídica de licenças e a assinatura/publicação dos binários.

**Ritmo sugerido:** uma fase por vez, com um marco de validação seu ao fim de cada fase (lista de verificação gerada na própria fase).

## 11. Assinatura de código (sem certificado)

A assinatura **não bloqueia o desenvolvimento** — só é necessária na F12, para distribuição. Até lá, os binários rodam sem assinatura (o Windows SmartScreen mostra "O Windows protegeu o computador"; basta clicar em "Mais informações → Executar assim mesmo").

Opções para a distribuição, em ordem de recomendação para um projeto GPL:

1. **SignPath Foundation** — assinatura de código **gratuita para projetos de código aberto**, com certificado emitido em nome da fundação. Requisitos: repositório público (ex.: GitHub), licença OSI (GPL atende), build reproduzível em CI (GitHub Actions). Combina com a decisão GPL.
2. **Certum "Open Source Code Signing"** — certificado de baixo custo para desenvolvedores de código aberto, emitido em seu nome (exige validação de identidade e token/cartão criptográfico ou assinatura em nuvem).
3. **Microsoft Artifact Signing (antigo Trusted Signing)** — serviço de assinatura em nuvem da Microsoft com assinatura mensal; verifique se a validação de identidade está disponível para pessoa física/empresa no Brasil antes de contratar.
4. **Certificado OV comercial** (Sectigo, DigiCert, GlobalSign etc.) — custo anual maior; chave obrigatoriamente em hardware (token/HSM) desde 2023.

Observações:
- Desde 2024, nem certificados EV dão reputação imediata no SmartScreen; a reputação cresce com downloads, independentemente do tipo de certificado.
- Alternativa complementar: publicar também via **WinGet** e/ou **Microsoft Store** (pacote MSIX, assinado pela própria Store).
- Para testes internos, é possível usar certificado autoassinado instalado manualmente nas máquinas de teste (nunca para distribuição pública).

**Recomendação:** hospedar o código em repositório público desde a F0 (exigência prática da GPL e da SignPath), montar o build no GitHub Actions e solicitar a SignPath Foundation ao chegar na F12.
