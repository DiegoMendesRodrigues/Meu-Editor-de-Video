# Modelos de IA local (F10)

Todos os recursos de inteligência artificial rodam **somente no computador do usuário** (RF155, RF169). Nenhum
áudio, vídeo, imagem ou texto é enviado para a internet, e o aplicativo não baixa modelos sozinho: eles são
instalados a partir de **pacotes `.medmodel` locais** (RF170), distribuídos separadamente do instalador (decisão §8 nº 5).
O teste de arquitetura `PrivacyTests` garante que nenhum assembly do produto, inclusive o `AIEngine`, usa APIs de rede.

## Recursos e modelos

| Recurso (aba **IA** das propriedades) | Requisitos | Capacidade | Modelo do catálogo |
|---|---|---|---|
| Remover fundo: transparente, cor sólida, desfoque, imagem ou vídeo da biblioteca; manter a pessoa ou o fundo; limiar, contração/expansão e suavização da borda | RF102–RF109, RF156, RF162 | `PersonSegmentation` | MODNet |
| Calcular máscaras em segundo plano (lote, com cache em memória e disco) | §8.2, F10 | `PersonSegmentation` | MODNet |
| Rastrear pessoa (gera um rastreamento usável na aba Composição) | RF163 | `PersonSegmentation` | MODNet |
| Gerar legendas (português, inglês, espanhol ou automático) | RF142, RF157, RF158 | `SpeechRecognition` | Whisper base |
| Isolar voz (voz e ambiente em trilhas separadas) | RF127, RF159 | `SpeechEnhancement` | GTCRN |
| Reduzir ruído (com intensidade) | RF160 | `SpeechEnhancement` | GTCRN |
| Remover trechos sem fala | RF165 | `VoiceActivity` | Silero VAD 5 |
| Detectar cenas → marcadores (e dividir o clipe) | RF161 | `SceneDetection` | TransNetV2; sem modelo, comparação de histogramas |
| Melhorar imagem (ampliar fotos 2×) | RF164 | `ImageEnhancement` | Swin2SR leve 2× |

Sem o modelo correspondente, o recurso aparece como **"Indisponível — instale o modelo em Ferramentas › Modelos de IA"**
e o botão fica desativado (§8.3). A detecção de cenas é a exceção: funciona sem modelo, com precisão menor.

## Gerenciador de modelos (Ferramentas › Modelos de IA)

- Lista os recursos de IA e se cada um está disponível, e os modelos instalados.
- Mostra todos os campos do §8.3: nome, identificador, versão, fabricante/origem, finalidade, formato, tamanho,
  memória necessária (com aviso se a memória livre for menor, RF168), compatibilidade com CPU e GPU, licença,
  hash SHA-256, integridade e localização, além da documentação que acompanha o pacote.
- **Instalar pacote…** valida o pacote antes de copiar: manifesto, licença obrigatória, versão do formato, nomes de
  arquivo seguros e **SHA-256 do modelo**. Pacotes corrompidos ou adulterados são recusados.
- **Verificar integridade** recalcula o hash do arquivo instalado; **Remover** desinstala.
- **Executar modelos em:** GPU (DirectML) ou somente CPU (RF167). Na GPU, se a placa ou o driver falharem, o modelo é
  carregado na CPU automaticamente. Modelos que declaram `gpu: false` sempre usam a CPU.

Os modelos ficam em `%LOCALAPPDATA%\MeuEditorDeVideo\ai-models\<id>\` e o cache de máscaras em
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

## Gerar os pacotes

1. Baixe os arquivos dos modelos das origens abaixo para uma pasta (ex.: `C:\modelos`).
2. Rode `.\tools\Build-ModelPackages.ps1 -ModelsDir C:\modelos -Output C:\pacotes`. O script lê
   `resources\ai-models\modelos.json` (metadados) e `resources\ai-models\licencas\` (licenças) e chama
   `tools\New-MedModel.ps1` para cada modelo. Para um modelo avulso, use `New-MedModel.ps1` diretamente
   (`Get-Help .\tools\New-MedModel.ps1 -Full`).

| Arquivo | Origem | Licença |
|---|---|---|
| `modnet.onnx` | huggingface.co/Xenova/modnet (`onnx/model.onnx`) | Apache-2.0 |
| `ggml-base.bin` | huggingface.co/ggerganov/whisper.cpp | MIT |
| `gtcrn_simple.onnx` | github.com/andrelgomes/gtcrn-stream (GTCRN de Xiaobin Rong) | MIT |
| `silero_vad.onnx` | github.com/snakers4/silero-vad | MIT |
| `transnetv2.onnx` | github.com/elya5/transnetv2 (TransNetV2 de Tomáš Souček) | MIT |
| `swin2sr-x2.onnx` | huggingface.co/Xenova/swin2SR-lightweight-x2-64 | Apache-2.0 |

## Detalhes de execução

- **ONNX Runtime 1.24.4 com DirectML** (`OnnxSessionFactory`): sessão por modelo, reaberta quando o modelo ou o
  dispositivo mudam (`ModelSessionHolder`).
- **Remover fundo** é o efeito `ai.background` na pilha de efeitos do clipe. O `FrameRenderer` o executa por meio de
  `IAiFrameProcessor` na posição em que está na pilha, por isso a pré-visualização e a exportação têm o mesmo resultado.
  As máscaras do MODNet (lado menor 512 px, múltiplos de 32) ficam em cache por quadro de origem da mídia, e assim
  servem tanto ao proxy quanto ao original. Os ajustes do efeito (limiar, manter fundo, contrair/expandir, suavizar)
  são editados na aba Vídeo.
- **Fala:** o áudio do clipe é renderizado como soa na timeline (velocidade e efeitos de áudio), convertido para mono
  16 kHz (FIR 63 taps) e processado:
  - Whisper.net/whisper.cpp na CPU;
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
- Whisper base é o modelo pequeno: a transcrição precisa de revisão no Editor de legendas, e modelos maiores (`small`,
  `medium`) podem ser empacotados com o mesmo script.
- A segmentação não tem suavização temporal, então pode haver leve oscilação na borda entre quadros. O MODNet é
  treinado para retratos e pode manter partes do fundo que lembram pessoas ou objetos próximos.
- Whisper e GTCRN rodam só na CPU.
