# Modelos de IA local (F10)

Todos os recursos de inteligência artificial rodam **somente no computador do usuário** (RF155, RF169). Nenhum
áudio, vídeo, imagem ou texto é enviado para a internet.

- **Incluído no aplicativo:** o **MODNet** (Remover fundo, Calcular máscaras, Rastrear pessoa) vem junto com o
  aplicativo, na pasta `ai-models\` ao lado do executável, e funciona desde a primeira execução.
- **Demais modelos:** são instalados a partir de **pacotes `.medmodel`** (RF170), distribuídos separadamente do
  instalador (decisão §8 nº 5). A ferramenta **Baixar modelos de IA** gera esses pacotes sem terminal (abaixo).

**Rede:** a única parte do aplicativo que acessa a internet é o assembly `MeuEditorDeVideo.ModelDownloads`
(decisão de 10/11/2026), usado só quando o usuário clica em **Instalar** na ferramenta de download. O teste de arquitetura
`PrivacyTests` garante que:
- nenhum outro assembly do produto (inclusive o `AIEngine` e o próprio aplicativo) usa ou referencia APIs de rede;
- o `ModelDownloads` depende só do `Core` (não tem acesso a mídias, projetos, áudio nem IA) e usa só HTTP.

## Recursos e modelos

| Recurso (aba **IA** das propriedades) | Requisitos | Capacidade | Modelo do catálogo |
|---|---|---|---|
| Remover fundo: transparente, cor sólida, desfoque, imagem ou vídeo da biblioteca; manter a pessoa ou o fundo; limiar, contração/expansão, suavização da borda e suavização entre quadros | RF102–RF109, RF156, RF162 | `PersonSegmentation` | MODNet (incluído) |
| Calcular máscaras em segundo plano (lote, com cache em memória e disco) | §8.2, F10 | `PersonSegmentation` | MODNet |
| Rastrear pessoa (gera um rastreamento usável na aba Composição) | RF163 | `PersonSegmentation` | MODNet |
| Gerar legendas (português, inglês, espanhol ou automático) | RF142, RF157, RF158 | `SpeechRecognition` | Whisper large-v3 turbo (+ Silero VAD, se instalado) |
| Isolar voz (voz e ambiente em trilhas separadas) | RF127, RF159 | `SpeechEnhancement` | GTCRN |
| Reduzir ruído (com intensidade) | RF160 | `SpeechEnhancement` | GTCRN |
| Remover trechos sem fala | RF165 | `VoiceActivity` | Silero VAD 5 |
| Detectar cenas → marcadores (e dividir o clipe) | RF161 | `SceneDetection` | TransNetV2; sem modelo, comparação de histogramas |
| Melhorar imagem (ampliar fotos 2×) | RF164 | `ImageEnhancement` | Swin2SR leve 2× |

Sem o modelo correspondente (exceto o MODNet, que é incluído), o recurso aparece como **"Indisponível — instale o modelo em Ferramentas › Modelos de IA"**
e o botão fica desativado (§8.3). A detecção de cenas é a exceção: funciona sem modelo, com precisão menor.

## Gerenciador de modelos (Ferramentas › Modelos de IA)

- Lista os recursos de IA e se cada um está disponível, e os modelos instalados.
- Mostra todos os campos do §8.3: nome, identificador, versão, fabricante/origem, finalidade, formato, tamanho,
  memória necessária (com aviso se a memória livre for menor, RF168), compatibilidade com CPU e GPU, licença,
  hash SHA-256, integridade e localização, além da documentação que acompanha o pacote.
- **Instalar pacote…** valida o pacote antes de copiar: manifesto, licença obrigatória, versão do formato, nomes de
  arquivo seguros e **SHA-256 do modelo**. Pacotes corrompidos ou adulterados são recusados.
- **Verificar integridade** recalcula o hash do arquivo instalado; **Remover** desinstala.
- Modelos **incluídos no aplicativo** aparecem como "incluído no aplicativo" e não podem ser removidos. Um pacote
  instalado com o mesmo identificador (ex.: uma versão nova do `modnet-retrato`) passa a ser usado no lugar do incluído;
  ao removê-lo, o incluído volta a valer. Na inicialização, o modelo incluído com arquivo ausente ou de tamanho
  diferente do manifesto é ignorado.
- **Executar modelos em:** GPU (DirectML) ou somente CPU (RF167). Na GPU, se a placa ou o driver falharem, o modelo é
  carregado na CPU automaticamente. Modelos que declaram `gpu: false` sempre usam a CPU.

Os modelos instalados ficam em `%LOCALAPPDATA%\MeuEditorDeVideo\ai-models\<id>\`, os incluídos em
`<pasta do aplicativo>\ai-models\<id>\`, e o cache de máscaras em
`%LOCALAPPDATA%\MeuEditorDeVideo\cache\mattes\`.

## Formato do pacote `.medmodel`

Um arquivo ZIP com, na raiz:

| Arquivo | Conteúdo |
|---|---|
| `manifest.json` | Manifesto (abaixo) |
| o modelo | `.onnx` (formato `ONNX`) ou `.bin` (formato `GGML`, Whisper) |
| `LICENSE.txt` | Texto da licença do modelo |
| `README.md` | Documentação de uso |

```json
{
  "id": "modnet-retrato",
  "name": "MODNet (recorte de pessoas)",
  "version": "1.0",
  "source": "ZHKKKe/MODNet (exportação ONNX Xenova/modnet)",
  "capability": "PersonSegmentation",
  "purpose": "Separa pessoas do fundo…",
  "format": "ONNX",
  "file": "modnet.onnx",
  "sizeBytes": 25888640,
  "memoryMb": 900,
  "cpu": true,
  "gpu": true,
  "license": "Apache-2.0",
  "licenseFile": "LICENSE.txt",
  "sha256": "…",
  "documentation": "README.md",
  "packageFormat": 1,
  "parameters": { "referenceSize": "512" }
}
```

`capability` é uma de: `PersonSegmentation`, `SpeechRecognition`, `SpeechEnhancement`, `VoiceActivity`,
`SceneDetection`, `ImageEnhancement`. Quando há mais de um modelo com a mesma capacidade, usa-se a versão mais alta.

## MODNet incluído no aplicativo

O arquivo `modnet.onnx` não fica no repositório. `scripts\fetch-deps.ps1` o baixa da revisão fixada em
`scripts\deps.lock.json` (com verificação do SHA-256), em tempo de desenvolvimento, para `deps\ai-models\modnet-retrato\`.
O build do aplicativo copia esse arquivo, junto com o manifesto, a licença e a documentação de
`resources\ai-models\incluidos\modnet-retrato\`, para `ai-models\modnet-retrato\` na pasta do executável. O teste
`Repository_built_in_manifest_matches_the_pinned_model` garante que o manifesto e o lock tenham o mesmo hash.

## Baixar modelos de IA (sem terminal)

Em **Ferramentas › Modelos de IA › Baixar modelos…**:

1. A ferramenta lista os modelos do catálogo oficial. Para cada um, mostra:
   - os recursos do editor que ele habilita;
   - o tamanho do download, a memória necessária, onde executa (CPU/GPU) e as dependências;
   - a licença, a origem e o site de onde é baixado;
   - a situação: não instalado, instalado ou versão nova disponível.
2. **Instalar** (ou **Instalar todos**) monta o plano: as dependências que faltam vêm antes de quem depende delas (ex.:
   Whisper → Silero VAD primeiro). Uma confirmação mostra a ordem e quanto será baixado.
3. Para cada modelo, nessa ordem, a ferramenta:
   1. baixa o arquivo do endereço fixado numa revisão da origem oficial (HTTPS, só `huggingface.co` e `github.com`);
   2. confere o tamanho e o **SHA-256** do catálogo;
   3. gera o pacote `.medmodel`, no mesmo formato de `tools\New-MedModel.ps1`, com licença e README;
   4. instala o pacote pelo gerenciador, com as mesmas validações de um pacote instalado à mão.
4. Um download interrompido é retomado de onde parou. Um arquivo com hash diferente é descartado. **Cancelar download**
   interrompe a instalação, e os modelos já instalados continuam valendo.

Os pacotes gerados ficam em `%LOCALAPPDATA%\MeuEditorDeVideo\modelos-baixados\pacotes\`. O botão **Abrir pasta dos
pacotes** abre essa pasta. Esses pacotes podem ser copiados para outro computador e instalados por **Instalar pacote…**,
sem internet. Se o pacote já existe e está íntegro, reinstalar não baixa de novo.

O catálogo fica em `resources\ai-models\modelos.json` e é copiado para `catalogo-ia\` ao lado do executável, junto com as
licenças. Cada entrada traz:
- `url`, fixada num commit, nunca em `main`;
- `sha256` e `sizeBytes`;
- `features` (recursos do editor);
- `dependsOn` (ids que devem ser instalados antes).

O teste `Catalogo_do_repositorio_e_valido_e_tem_licencas` valida o catálogo.

| Modelo | Download | Depende de |
|---|---|---|
| Whisper large-v3 turbo | 547,4 MB | Silero VAD 5 |
| GTCRN | 523 KB | — |
| Silero VAD 5 | 2,2 MB | — |
| TransNetV2 | 29,8 MB | — |
| Swin2SR leve 2× | 7,7 MB | — |

## Gerar os pacotes (alternativa pelo terminal)

1. Baixe os arquivos dos modelos das origens abaixo para uma pasta (ex.: `C:\modelos`).
2. Rode `.\tools\Build-ModelPackages.ps1 -ModelsDir C:\modelos -Output C:\pacotes`. O script lê
   `resources\ai-models\modelos.json` (metadados) e `resources\ai-models\licencas\` (licenças) e chama
   `tools\New-MedModel.ps1` para cada modelo. Para um modelo avulso, use `New-MedModel.ps1` diretamente
   (`Get-Help .\tools\New-MedModel.ps1 -Full`).

| Arquivo | Origem | Licença |
|---|---|---|
| `ggml-large-v3-turbo-q5_0.bin` | huggingface.co/ggerganov/whisper.cpp | MIT |
| `gtcrn_simple.onnx` | github.com/andrelgomes/gtcrn-stream (GTCRN de Xiaobin Rong) | MIT |
| `silero_vad.onnx` | github.com/snakers4/silero-vad | MIT |
| `transnetv2.onnx` | github.com/elya5/transnetv2 (TransNetV2 de Tomáš Souček) | MIT |
| `swin2sr-x2.onnx` | huggingface.co/Xenova/swin2SR-lightweight-x2-64 | Apache-2.0 |

## Detalhes de execução

- **ONNX Runtime 1.24.4 com DirectML** (`OnnxSessionFactory`): sessão por modelo, reaberta quando o modelo ou o
  dispositivo mudam (`ModelSessionHolder`).
- **Remover fundo** é o efeito `ai.background` na pilha de efeitos do clipe. O `FrameRenderer` o executa por meio de
  `IAiFrameProcessor` na posição em que está na pilha, por isso a pré-visualização e a exportação têm o mesmo resultado.
  As máscaras do MODNet (lado menor 512 px, múltiplos de 32) são calculadas sempre a partir do quadro de origem
  decodificado em até 960×540 (um decodificador próprio, com leitura sequencial) e ficam em cache por quadro de origem
  da mídia. Assim, o tamanho da prévia, o proxy e a resolução da exportação não mudam a máscara.
- **Suavização entre quadros** (parâmetro do efeito, padrão 50 %): a máscara de cada quadro é misturada com as dos
  quadros vizinhos (±1 quadro até 39 %, ±2 a partir de 40 %), com peso gaussiano pela distância no tempo e, em cada
  pixel, pela semelhança com o quadro central. Diferenças pequenas (a borda que oscila) são suavizadas; diferenças
  grandes (a pessoa se moveu) ficam fora da média, sem rastro. O resultado depende só das máscaras de origem, então é
  o mesmo na reprodução, ao saltar na timeline e na exportação. Em 0 % fica desligada.
- Os ajustes do efeito (limiar, manter fundo, contrair/expandir, suavizar borda, suavização entre quadros) são editados
  na aba Efeitos.
- **Fala:** o áudio do clipe é renderizado como soa na timeline (velocidade e efeitos de áudio), convertido para mono
  16 kHz (FIR 63 taps) e processado:
  - Whisper.net/whisper.cpp na CPU com large-v3 turbo quantizado (q5_0, 547 MB). Antes de transcrever, os trechos com
    fala são localizados (Silero VAD quando instalado; sem ele, só o silêncio abaixo de −50 dBFS é descartado) e
    agrupados em blocos (pausas de até 3 s, blocos de até 28 s). Só os blocos são transcritos, o que evita frases
    inventadas no silêncio e prende o início das legendas ao início real da fala;
  - Silero VAD em janelas de 512 amostras com 64 de contexto, com histerese;
  - GTCRN quadro a quadro: STFT 512/256 com janela raiz de Hann, reconstrução exata.
  Os WAVs gerados (48 kHz estéreo) vão para `Mídias geradas (IA)`, ao lado do projeto, são importados e colocados em
  trilhas novas; o original pode ser silenciado. Tudo isso fica numa única ação de desfazer.
- **Cenas:** o TransNetV2 recebe janelas de 100 quadros 48×27 e usa os 50 centrais; os picos acima do limiar (ajustado
  pela sensibilidade) viram marcadores "Cena N", com distância mínima de 0,5 s.
- **Melhorar imagem:** o Swin2SR processa a imagem em blocos de 192 px com 16 px de sobreposição e mistura linear; o
  alfa é ampliado por interpolação. O clipe passa a usar a nova imagem PNG, e desfazer volta à original.

## Limitações conhecidas

- O isolamento de voz trabalha em 16 kHz: a trilha de voz não tem conteúdo acima de 8 kHz, e a trilha de ambiente
  contém esses agudos da voz.
- Whisper large-v3 turbo leva cerca de 0,4 s de processamento por segundo de áudio na CPU (≈ 4 min para 10 min).
  Sem o Silero VAD instalado, um trecho só de ruído ainda pode gerar uma frase curta inventada.
- O MODNet é treinado para retratos e pode manter partes do fundo que lembram pessoas ou objetos próximos. Com a
  suavização entre quadros, a primeira exibição de um quadro fora do cache segmenta também os vizinhos (mais lenta ao
  saltar na timeline; na reprodução contínua, é um quadro novo por quadro).
- Whisper e GTCRN rodam só na CPU.
