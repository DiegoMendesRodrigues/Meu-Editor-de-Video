# Desempenho, hardware e configurações (F11)

Este documento cobre os requisitos RF208–RF214, RNF008, RNF009, RNF013 e o §12.3 da especificação.

## 1. Detecção de hardware (§12.3)

O `HardwareDetector` (Infrastructure) lê as informações do computador **localmente**, sem acesso à rede:

| Item | Fonte |
|---|---|
| Processador e núcleos lógicos | Registro (`HARDWARE\DESCRIPTION\System\CentralProcessor\0`) e `Environment.ProcessorCount` |
| Memória total e livre | `GlobalMemoryStatusEx` |
| Placas de vídeo e memória de vídeo | Chaves da classe de vídeo no registro (`HardwareInformation.qwMemorySize`); placas de software ou "básicas" não contam como dedicadas |
| Espaço em disco | `DriveInfo` das pastas de cache e de projetos |
| Codificação por hardware | `EncoderCatalog.HardwareSummary()`: NVENC, AMF, Quick Sync e Media Foundation testados de verdade |
| Decodificação por hardware | `HardwareProbe.D3D11VideoDecoding()` (`av_hwdevice_ctx_create` D3D11VA) |

A primeira detecção roda em segundo plano na inicialização. O resultado fica guardado, e só a memória livre e os discos são relidos quando a tela de configurações pede atualização.

## 2. Ajuste automático (`PerformanceAdvisor`)

| Nível | Critério | Pré-visualização (até Full HD / acima) | Memória para quadros |
|---|---|---|---|
| Alto (§12.2) | RAM ≥ 31 GB, GPU dedicada ≥ 7,5 GB, ≥ 12 núcleos lógicos | Completa até QHD; 1/2 em 4K | 1 GB |
| Intermediário (§12.1) | RAM ≥ 15 GB, GPU dedicada ≥ 3,5 GB, ≥ 8 núcleos lógicos | 1/2 até Full HD; 1/4 acima | 512 MB |
| Básico | abaixo do mínimo | 1/2 até 720p; 1/4 acima | 256 MB |

- A memória para quadros nunca passa de 10% da RAM (mínimo de 128 MB).
- O Windows informa um pouco menos que o valor nominal (16 GB aparecem como cerca de 15,8 GB), e os limites levam isso em conta.
- **Limitações indicadas ao usuário:** pouca RAM, sem GPU dedicada, pouca memória de vídeo, poucos núcleos, sem codificador ou decodificador por hardware, disco com menos de 20 GB livres, e projeto 4K fora do nível alto.

**Qualidade "Automática" da pré-visualização:**
- O divisor de resolução começa pela recomendação acima, considerando a resolução do projeto.
- Durante a reprodução, se a média de 24 quadros passar de 1,5× a duração de um quadro, o divisor dobra (até 1/4).
- O aviso "Qualidade reduzida automaticamente para …" aparece abaixo do monitor.

**Memória (RNF013):**
- O cache de quadros prontos do `PreviewEngine` é limitado por bytes (`CacheBudgetBytes`), não por número de quadros. Por isso um projeto 4K não ocupa 16 vezes mais memória que um em 720p.
- O cache em disco (miniaturas, formas de onda, proxies e máscaras de IA) tem um limite em GB. O `CacheMaintenance` apaga primeiro os itens usados há mais tempo.
  - A limpeza roda na inicialização e quando o limite é reduzido.
  - Ela nunca apaga itens usados nos últimos 2 minutos nem arquivos de projeto.

## 3. Perfil 4K multitrilha

Teste `PerformanceProfileTests` (MediaTests, categoria "Desempenho"):
- **Projeto:** 3840×2160 a 30 q/s, com três trilhas de vídeo 4K H.264 sobrepostas: tela cheia, uma com 50% de opacidade e saturação, e um PiP a 40%.
- **Renderização:** compositor de CPU.
- **Medição:** média de 12 quadros depois do aquecimento.
- **Máquina de desenvolvimento:** Ryzen 9 5900X (24 núcleos lógicos), 64 GB, RTX 3070 8 GB, que corresponde ao nível alto.

| Qualidade | Resolução | ms/quadro | Quadros/s | Quadro pronto |
|---|---|---|---|---|
| Completa | 3840×2160 | 85,5 | 11,7 | 31,6 MB |
| 1/2 | 1920×1080 | 27,8 | 36,0 | 7,9 MB |
| 1/4 | 960×540 | 8,5 | 117,8 | 2,0 MB |

**O que os números mostram:**
- Para um projeto 4K no nível alto, a recomendação é **1/2**: 36 q/s acompanha a reprodução a 30 q/s. A resolução completa (11,7 q/s) serve para quadros parados e para a exportação.
- O custo cai quase na proporção do número de pixels (3,1× de Completa para 1/2 e 3,3× de 1/2 para 1/4), por isso reduzir a resolução compensa.
- Com 1 GB, o cache guarda cerca de 32 quadros 4K completos ou cerca de 130 quadros em 1/2.

Para repetir a medição:

```
dotnet test tests\MeuEditorDeVideo.MediaTests --filter "Categoria=Desempenho" --logger "console;verbosity=detailed"
```

O relatório fica em `%TEMP%\med-desempenho\perfil-4k.txt`.

## 4. Tela de configurações (Ferramentas → Configurações…, Ctrl+,)

| Aba | Conteúdo | Requisito |
|---|---|---|
| Geral | Tema; abrir o último projeto ao iniciar; pastas padrão de projetos e exportações; pasta do cache; salvamento automático e intervalo | RF207, RF208, RF213 |
| Desempenho | Hardware detectado, nível e limitações, recomendação; qualidade padrão da pré-visualização; memória para quadros; proxies; aceleração por GPU; renderização em segundo plano; dispositivo da IA e acesso ao gerenciador de modelos | RF210, RF211, RF214, §12.3 |
| Cache | Limite em GB, uso por área, "Aplicar o limite agora" e "Limpar cache" | RF209 |
| Atalhos | Todas as ações por categoria: capturar uma nova combinação, restaurar o padrão, remover, restaurar todos | RF212 |

**Funcionamento:**
- Nada é gravado até clicar em **Salvar**.
- Atalhos em conflito são indicados ação a ação e impedem salvar.
- Só os atalhos alterados ficam em `settings.json` (`shortcuts`); um texto vazio significa "sem atalho".
- Os menus mostram sempre o atalho em vigor.
