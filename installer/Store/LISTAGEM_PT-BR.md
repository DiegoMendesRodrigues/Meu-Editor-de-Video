# Listagem da Microsoft Store — Português (Brasil)

Textos prontos para copiar em **Partner Center › (o aplicativo) › Envio › Listagens da Store › Português (Brasil)**.
Os limites entre parênteses são os do Partner Center.

---

## Nome do produto

Meu Editor de Vídeo

> Precisa ser exatamente o nome reservado no Partner Center e o mesmo de `installer/Store/store.json` (`displayName`).

## Descrição (até 10.000 caracteres)

```text
Meu Editor de Vídeo é um editor de vídeo completo para Windows 11, todo em português, que faz tudo no seu computador — inclusive a inteligência artificial. Seus vídeos, fotos, áudios e projetos nunca são enviados para a internet.

EDITE COM LIBERDADE
Monte seu vídeo em uma linha do tempo com várias trilhas de vídeo, imagem, áudio, texto e legenda. Corte, divida, mova e apare clipes com precisão de quadro, use o ímã e a edição em cascata, agrupe clipes, marque pontos importantes e desfaça qualquer ação pelo histórico.

IMPORTE PRATICAMENTE TUDO
Vídeos MP4, MOV, MKV, WebM, AVI e muitos outros; áudios MP3, WAV, AAC, FLAC, OGG e Opus; imagens JPG, PNG, WebP, TIFF e SVG; GIFs animados e sequências de imagens. Proxies leves deixam a edição fluida mesmo com vídeos 4K.

EFEITOS, COR E COMPOSIÇÃO
Transições, ajustes de cor e LUTs, desfoque, chroma key, máscaras animadas, rastreamento de movimento, estabilização, câmera lenta com interpolação, quadros-chave e composição 2,5D com perspectiva.

TEXTOS E LEGENDAS
Títulos, créditos, tarjas e textos animados com contorno e sombra. Importe legendas SRT e VTT, edite no editor de legendas e exporte em arquivo ou gravadas no vídeo.

ÁUDIO DE QUALIDADE
Mixer com medidores, volume com quadros-chave, fades, equalizador, compressor, normalização de loudness (EBU R128), redução de ruído e sincronização automática de clipes pelo áudio.

INTELIGÊNCIA ARTIFICIAL LOCAL
• Remover o fundo de pessoas sem tela verde (modelo incluído).
• Gerar legendas automaticamente a partir da fala.
• Isolar a voz e reduzir ruído.
• Remover trechos sem fala.
• Detectar mudanças de cena.
• Melhorar a nitidez da imagem.
Os modelos opcionais são baixados uma única vez, dos sites oficiais dos autores, apenas quando você pede. Depois disso, tudo funciona sem internet. Usa a placa de vídeo (DirectML) quando disponível.

EXPORTE PARA ONDE QUISER
Modelos de exportação prontos para as principais plataformas de vídeo e redes sociais, para apresentações e para computador, ou configure tudo: formato, codec (H.264, H.265, AV1 e outros), resolução, taxa de quadros e qualidade. Codificação acelerada por placa de vídeo NVIDIA, AMD e Intel, fila de renderização e exportação em segundo plano.

SEGURO E PRIVADO
Sem conta, sem anúncios e sem telemetria. Salvamento automático e recuperação do projeto após um fechamento inesperado.

Software livre, licenciado sob a GNU GPL versão 3 ou posterior.
```

## Descrição curta (até 1.000 caracteres)

```text
Editor de vídeo completo, em português, com inteligência artificial que roda 100% no seu computador: remova fundos, gere legendas, isole vozes e exporte para qualquer plataforma — sem enviar nada para a internet.
```

## O que há de novo nesta versão (até 1.500 caracteres)

```text
Primeira versão na Microsoft Store.
```

## Recursos do produto (até 20 itens, 200 caracteres cada)

```text
Linha do tempo com várias trilhas de vídeo, imagem, áudio, texto e legenda
Remoção de fundo com IA, sem tela verde
Legendas automáticas geradas a partir da fala, no seu computador
Isolamento de voz e redução de ruído com IA
Detecção de cenas e remoção de trechos sem fala
Transições, efeitos, correção de cor e LUTs
Máscaras animadas, rastreamento de movimento e estabilização
Composição 2,5D com perspectiva e quadros-chave
Textos, títulos, créditos e legendas SRT/VTT
Mixer de áudio, equalizador, compressor e normalização de loudness
Exportação em H.264, H.265, AV1 e outros formatos, com aceleração por GPU
Modelos de exportação prontos para redes sociais e plataformas de vídeo
Proxies para editar vídeos 4K com fluidez
Salvamento automático e recuperação de projetos
Processamento 100% local: sem conta, sem anúncios e sem telemetria
```

## Palavras-chave de pesquisa (até 7)

```text
editor de vídeo
editar vídeo
legendas automáticas
remover fundo
cortar vídeo
montagem de vídeo
inteligência artificial
```

## Requisitos do sistema

| | Mínimo | Recomendado |
|---|---|---|
| Sistema | Windows 11 64 bits | Windows 11 64 bits |
| Processador | Intel Core i5 ou AMD Ryzen 5 | Intel Core i7 ou AMD Ryzen 7 |
| Memória | 16 GB | 32 GB |
| Placa de vídeo | Compatível com Direct3D 11 | NVIDIA RTX ou AMD Radeon com 8 GB |
| Armazenamento | SSD | SSD NVMe |

> No Partner Center, em **Requisitos do sistema**, marque "Mínimo" e "Recomendado" para: Memória (16 GB / 32 GB),
> DirectX (DirectX 11) e Arquitetura (x64). Os demais campos podem ficar em branco.

## Outros campos

| Campo | Valor sugerido |
|---|---|
| Direitos autorais e marca comercial | `Copyright © 2026 Diego Mendes Rodrigues. Licenciado sob GPL-3.0-or-later.` |
| Termos de licença adicionais | Deixe em branco (usa os termos padrão da Store) ou informe o link para o arquivo `LICENSE` publicado junto do código-fonte. |
| Site | Endereço onde o código-fonte e o manual estão publicados. |
| Informações de contato de suporte | Seu e-mail ou página de suporte (obrigatório). |
| URL da política de privacidade | Endereço público de `POLITICA_DE_PRIVACIDADE.md` (ver `documentation/MICROSOFT_STORE.md`). |

## Imagens

| Tipo | Arquivos |
|---|---|
| Capturas de tela para área de trabalho (1 a 10, PNG, mínimo 1366 × 768) | `installer/Store/capturas/*.png` (1920 × 1020, dados de demonstração) |
| Logotipos da Store (opcionais) | `artifacts/store/listagem/logotipo-300x300.png`, `-150x150`, `-71x71` (gerados por `scripts/build-msix.ps1`) |

Legendas sugeridas para as capturas (até 200 caracteres cada):

1. `Tela inicial: crie um projeto ou continue de onde parou.`
2. `Edição em várias trilhas, com pré-visualização em tempo real.`
3. `Exportação com modelos prontos para cada plataforma.`
