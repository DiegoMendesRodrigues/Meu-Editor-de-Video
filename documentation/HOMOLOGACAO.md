# Homologação — Meu Editor de Vídeo 1.0.0

Verificação dos critérios de aceitação CA001–CA024 (§15), dos requisitos não funcionais RNF017–RNF020 e dos entregáveis
ENT001–ENT018 (§16). Executada em **10/11/2026** (fase F12).

## Ambiente

| Item | Valor |
|---|---|
| Sistema | Windows 11 Pro 64 bits (10.0.26300), pt-BR |
| Processador / memória | AMD Ryzen 9 5900X (24 núcleos lógicos) / 64 GB |
| Placa de vídeo | NVIDIA GeForce RTX 3070, 8 GB (NVENC, D3D11VA, DirectML) |
| Voz do Windows | Microsoft Maria (pt-BR), usada para gerar fala nos testes do Whisper |
| Compilação | .NET SDK 10, Debug e Release; 0 avisos, 0 erros |

## Resultado dos testes automatizados

Todos os modelos de IA opcionais instalados pela própria ferramenta **Baixar modelos de IA** (5 modelos, 587,7 MB, das
origens oficiais, com SHA-256), para que nenhum teste fosse ignorado.

| Projeto | Testes | Aprovados | Falhas | Ignorados |
|---|---|---|---|---|
| `MeuEditorDeVideo.UnitTests` | 842 | 842 | 0 | 0 |
| `MeuEditorDeVideo.IntegrationTests` | 38 | 38 | 0 | 0 |
| `MeuEditorDeVideo.MediaTests` | 111 | 111 | 0 | 0 |
| **Total** | **991** | **991** | **0** | **0** |

Sem os modelos opcionais, 6 testes que dependem deles são ignorados automaticamente (os demais 985 passam).

> Observação: numa das execuções da solução inteira (sem os modelos), 1 teste unitário falhou uma única vez. A falha não se
> repetiu em cerca de 15 execuções seguidas, inclusive com os projetos rodando em paralelo e sob carga, e o nome do teste
> não ficou registrado. Se voltar a ocorrer, anotar o nome (`dotnet test --logger "console;verbosity=normal"`) para investigar.

Bateria offline (`scripts/offline-check.ps1`) durante o roteiro de interface da F12: 212 amostras em 75 s, **nenhuma
conexão TCP/UDP externa**.

### Defeitos encontrados e corrigidos na F12

| Defeito | Encontrado por | Correção |
|---|---|---|
| Qualquer JPEG era classificado como "danificado" na importação e, na pré-visualização, saía preto | Matriz audiovisual (§14.3) | `MediaProbe.VerifyDecodable` e `VideoFrameReader.Position` não fazem mais busca até o quadro 0 num leitor recém-aberto (o demuxer de JPEG perdia o único pacote) |
| Exportar um intervalo que não começa no início, sem dividir o arquivo, falhava ("Nenhum trecho para multiplexar") | Integração Render ↔ FFmpeg (§14.2) | `RenderEngine.SplitParts`: estouro de `long.MaxValue` |
| Mídia danificada era exportada como quadros pretos, sem erro | Recuperação (§14.5) | `IVideoFrameSource.Failure`; a exportação para com MED-3003 em pt-BR e não deixa arquivo parcial |
| Seção **IA** da biblioteca dizia "disponível em uma próxima versão" | Revisão do manual | Virou um guia dos recursos com o botão **Gerenciar modelos de IA…** |

## Critérios de aceitação (§15)

Legenda: **Aprovado** = verificado por testes automatizados e/ou roteiro de interface; **Aprovado com ressalva** = verificado
neste ambiente, com uma parte que depende de validação manual em outro equipamento (descrita).

| ID | Critério | Situação | Evidências |
|---|---|---|---|
| CA001 | Instalação e execução no Windows 11 | Aprovado com ressalva | MSI instalado e removido em modo silencioso "somente para mim" no Windows 11 ([INSTALADOR.md](INSTALADOR.md#testes-do-instalador-f12)); o aplicativo instalado abriu um `.med` pela associação. **Ressalva:** o `Setup.exe` (com o Visual C++) foi gerado, mas a instalação "para todos os usuários" com elevação deve ser conferida manualmente num Windows 11 limpo. |
| CA002 | Interface em pt-BR | Aprovado | Textos em `Resources/Strings/*.xaml` e XAML em pt-BR; cultura pt-BR forçada na inicialização; mensagens de erro `MedErrors` em pt-BR; instalador com `Language=1046` e `Cultures=pt-BR`; roteiros UIA de todas as fases (F1–F12) localizam os controles pelos nomes em português. |
| CA003 | Criação e abertura de `.med` | Aprovado | `MedProjectStoreTests`, `ProjectServiceTests`, `ProjectMedIntegrationTests` (salvar, fechar e reabrir vinculado e empacotado; `.med` corrompido gera erro em pt-BR); associação `.med` do instalador; `StartupArgumentsTests`. |
| CA004 | Importação dos formatos previstos | Aprovado | `MediaProbeTests`, `MediaImportServiceTests`, `AudiovisualMatrixTests` (H.264, HEVC, VP9, AV1 SVT/aom, MPEG-4, MJPEG, ProRes, PNG, JPEG; WAV, AAC, MP3, FLAC, Opus), `SvgTests`, sequências de imagens e GIF. |
| CA005 | Timeline com múltiplas trilhas | Aprovado | `TimelineCommandsTests`, `SnapEngineTests`, `TimelineEvaluatorTests`, `InterfaceTimelineIntegrationTests`, matriz audiovisual (3 trilhas de vídeo + 2 de áudio). |
| CA006 | Reprodução e pré-visualização | Aprovado | `PlaybackSourceTests`, `TimelineMediaIntegrationTests`, `PerformanceProfileTests` (perfil 4K multitrilha), roteiros UIA da F4/F11. |
| CA007 | Cortes e aparagem precisos | Aprovado | `TimelineCommandsTests` (dividir, aparar, ripple, quadro exato em tempo racional), `TimeTests`, `InterfaceTimelineIntegrationTests`. |
| CA008 | Transições e efeitos | Aprovado | `EffectCatalogTests`, `EffectProcessorTests`, `CompositorVisualTests`, `VisualRenderTests`. |
| CA009 | Tratamento de imagem e cores | Aprovado | `EffectProcessorTests` (cor, curvas, LUT), `CompositorVisualTests`, `VisualRenderTests`. |
| CA010 | Remoção e substituição de fundo | Aprovado | `VisionTests` (transparente, cor, imagem, desfoque, suavização temporal, cache de máscaras), `AiOnnxIntegrationTests`, `AiDeviceTests` com o MODNet incluído. |
| CA011 | Chroma key | Aprovado | `EffectProcessorTests`, `EffectCatalogTests` (verde, azul, cor livre, tolerância, despill), `EdgeRefineTests`. |
| CA012 | Edição e mixagem de áudio | Aprovado | `AudioEditingTests`, `AudioMixerTests`, `AudioProcessorTests`, `LoudnessAnalysisTests` (EBU R128), `BiquadFftTests`, `AudioRenderTests`. |
| CA013 | Textos e legendas | Aprovado | `TextTests`, `SubtitleFileTests` (SRT/VTT), `SpeechRecognitionTests` (legendas geradas pelo Whisper). |
| CA014 | Animações e rastreamento | Aprovado | `ClipAnimationTests`, `AnimationPresetsTests`, `CompositionTests` (rastreamento de movimento OpenCV, vínculo), `VisionTests` (caixa da pessoa). |
| CA015 | IA executada localmente | Aprovado | Todos os recursos com modelos reais: MODNet (`VisionTests`, `AiOnnxIntegrationTests`), Whisper em pt-BR (`SpeechRecognitionTests`), GTCRN, Silero VAD (`SpeechTests`), TransNetV2, Swin2SR (`VisionTests`); interrupção da IA (`RecuperacaoTests`). |
| CA016 | Sem transmissão de conteúdo | Aprovado | `PrivacyTests` (somente `MeuEditorDeVideo.ModelDownloads` usa rede, e só baixa modelos quando o usuário clica em Instalar; nada é enviado); bateria offline sem nenhuma conexão. |
| CA017 | 12 modelos de exportação | Aprovado | `ExportFormatsTests`, `ExportTemplateStoreTests` (EXP001–EXP012 em `resources/templates/export`). |
| CA018 | Exportação nos formatos previstos | Aprovado | `RenderEngineTests`, `RenderFfmpegIntegrationTests` (MP4 com duração, resolução e áudio; intervalo), `AudiovisualMatrixTests` (taxas de quadros), `CompositorAlphaTests` (alfa). |
| CA019 | Renderização em segundo plano | Aprovado | `RenderQueueServiceTests` (fila, cancelar, falhar, retomar), roteiro UIA da F5. |
| CA020 | Recuperação de projetos | Aprovado | `AutosaveStoreTests`, `AutosaveSchedulerTests`, `RecuperacaoTests` (encerramento inesperado, falha ao salvar, falta de espaço, mídia ausente, falha na renderização, interrupção da IA). |
| CA021 | Funcionamento com e sem GPU dedicada | Aprovado com ressalva | Com GPU: NVENC, D3D11VA e DirectML neste equipamento. Sem GPU: recuos automáticos testados (`AiDeviceTests`: GPU indisponível ou modelo sem GPU → CPU com a mesma máscara; codificação por software em `RenderEngineTests` com `EncoderPreference.Software`; decodificação sem hardware nos testes de mídia). **Ressalva:** executar o roteiro básico num computador sem placa dedicada. |
| CA022 | Instalador completo e funcional | Aprovado com ressalva | MSI: instalação, atalhos, associação `.med`, entrada em Aplicativos, desinstalação limpa. `Setup.exe` gerado com o Visual C++ encadeado. **Ressalva:** mesma do CA001 (instalação elevada do `Setup.exe`). |
| CA023 | Documentação técnica e operacional | Aprovado | [DOCUMENTACAO_TECNICA.md](DOCUMENTACAO_TECNICA.md), [MANUAL_DO_USUARIO.md](MANUAL_DO_USUARIO.md) (também em **Ajuda › Manual do usuário**, F1), [INSTALADOR.md](INSTALADOR.md), [MODELOS_IA.md](MODELOS_IA.md), [DESEMPENHO.md](DESEMPENHO.md), [DEPENDENCIAS.md](DEPENDENCIAS.md). |
| CA024 | Testes funcionais e de integração aprovados | Aprovado | 991/991 (tabela acima); roteiros UIA com capturas de tela em todas as fases. |

## Requisitos não funcionais RNF017–RNF020

| ID | Requisito | Situação |
|---|---|---|
| RNF017 | Instalação e desinstalação convencionais | MSI (Windows Installer) e `Setup.exe`; desinstalação pelo Windows, sem resíduos de programa; dados do usuário preservados. |
| RNF018 | Documentação técnica | [DOCUMENTACAO_TECNICA.md](DOCUMENTACAO_TECNICA.md). |
| RNF019 | Testes automatizados | 991 testes em 3 projetos, executados pela CI (`.github/workflows/build.yml`), que também gera o instalador. |
| RNF020 | Respeito às licenças | Produto GPL-3.0-or-later (compatível com o FFmpeg GPL); `THIRD-PARTY-NOTICES.txt` gerado a partir dos pacotes distribuídos e instalado com o aplicativo (**Ajuda › Licenças de terceiros**); licença de cada modelo no pacote `.medmodel`; tela de licença no instalador; [DEPENDENCIAS.md](DEPENDENCIAS.md). |

## Entregáveis (§16)

| ID | Entregável | Onde |
|---|---|---|
| ENT001 | Código-fonte completo | Repositório (`src/`, `tests/`, `resources/`, `scripts/`, `installer/`) |
| ENT002 | Solução Visual Studio | `MeuEditorDeVideo.sln` |
| ENT003 | Aplicativo executável | `MeuEditorDeVideo.exe` (publicação em `artifacts\publish`) |
| ENT004 | Instalador para Windows 11 | `artifacts\installer\MeuEditorDeVideo-1.0.0-Setup.exe` e `…-x64.msi` (`scripts/build-installer.ps1`) |
| ENT005 | Motor de processamento audiovisual | `src/MeuEditorDeVideo.MediaEngine` |
| ENT006 | Motor de Timeline | `src/MeuEditorDeVideo.TimelineEngine` |
| ENT007 | Motor de renderização | `src/MeuEditorDeVideo.RenderingEngine` |
| ENT008 | Motor de processamento de imagem | `src/MeuEditorDeVideo.ImageEngine` |
| ENT009 | Motor de processamento de áudio | `src/MeuEditorDeVideo.AudioEngine` |
| ENT010 | Motor de IA local | `src/MeuEditorDeVideo.AIEngine` |
| ENT011 | Formato de projeto `.med` | `src/MeuEditorDeVideo.ProjectEngine`; descrito na documentação técnica |
| ENT012 | Biblioteca dos modelos de exportação | `resources/templates/export` (EXP001–EXP012) |
| ENT013 | Modelos locais de IA e manifestos | MODNet incluído (`resources/ai-models/incluidos`); catálogo `resources/ai-models/modelos.json` com licenças |
| ENT014 | Biblioteca de efeitos e transições | `src/MeuEditorDeVideo.Domain/Effects/EffectCatalog.cs` (catálogo) e `src/MeuEditorDeVideo.ImageEngine` (processamento) |
| ENT015 | Testes automatizados | `tests/` |
| ENT016 | Documentação técnica | `documentation/DOCUMENTACAO_TECNICA.md` |
| ENT017 | Manual de utilização | `documentation/MANUAL_DO_USUARIO.md` (instalado em `ajuda\`) |
| ENT018 | Dependências e licenças | `documentation/DEPENDENCIAS.md`, `THIRD-PARTY-NOTICES.txt` |

## Pendências para o responsável do produto

1. Instalar o `Setup.exe` num Windows 11 limpo, como administrador (confere o Visual C++ encadeado e a instalação para todos os usuários).
2. Executar o roteiro básico (importar, editar, remover fundo, exportar) num computador sem placa de vídeo dedicada.
3. Obter um certificado de assinatura de código quando desejar eliminar o aviso do SmartScreen ([INSTALADOR.md](INSTALADOR.md#assinatura-de-código)).
