# Manual do usuário — Meu Editor de Vídeo

Este manual explica como instalar, usar e configurar o Meu Editor de Vídeo. Ele foi escrito para quem nunca editou vídeo antes. Os nomes de menus e botões são os mesmos que aparecem na tela.

Os textos em **negrito** são nomes de menus, botões e opções. O símbolo › separa os níveis de um menu: **Arquivo › Exportar…** significa abrir o menu Arquivo e escolher Exportar.

## Primeiros passos

O Meu Editor de Vídeo roda no Windows 11 de 64 bits. Para instalar, use um dos dois arquivos de instalação:

- **MeuEditorDeVideo-…-Setup.exe**: instalador completo. Recomendado para a maioria das pessoas.
- **MeuEditorDeVideo-…-x64.msi**: pacote do Windows Installer, para quem prefere instalar pelo padrão do Windows.

Execute o arquivo, siga as telas e, ao final, abra o programa pelo menu Iniciar. Não é preciso instalar mais nada separadamente.

> Se o Windows mostrar o aviso "O Windows protegeu o computador", clique em "Mais informações" e depois em "Executar assim mesmo". Esse aviso aparece em programas sem certificado de assinatura digital.

### Requisitos do computador

Estes são os requisitos previstos na especificação do produto:

- **Mínimo:** Windows 11 de 64 bits, processador Intel Core i5 ou AMD Ryzen 5, 16 GB de memória RAM, placa de vídeo compatível com Direct3D e SSD com pelo menos 512 GB.
- **Recomendado:** Windows 11 de 64 bits, processador Intel Core i7 ou AMD Ryzen 7, 32 GB ou mais de memória RAM, placa de vídeo NVIDIA RTX ou AMD Radeon com 8 GB ou mais de memória de vídeo e SSD NVMe de 1 TB ou mais.

Uma placa de vídeo dedicada não é obrigatória para o programa funcionar, mas deixa a pré-visualização, a exportação e os recursos de inteligência artificial bem mais rápidos.

### A tela inicial

Ao abrir o programa, aparece a tela inicial. Ela tem estes elementos:

- **Novo Projeto**: cria um projeto de vídeo novo.
- **Abrir Projeto**: abre um projeto salvo anteriormente (arquivo com a extensão .med).
- **Configurações**: abre as configurações do programa.
- **Projetos recentes**: lista os últimos projetos abertos. Clique num deles para abrir. Se o arquivo tiver sido movido ou apagado, aparece a mensagem "Arquivo não encontrado".
- **Recuperação**: aparece uma faixa com o botão **Recuperar** e o botão **Descartar** quando existe uma cópia de um projeto que não foi salvo corretamente. Veja a seção Salvar, salvamento automático e recuperação.

### Criar um projeto

1. Na tela inicial, clique em **Novo Projeto**.
2. Escreva um nome em "Nome do projeto".
3. Escolha a pasta onde o projeto será guardado em "Pasta do projeto" (use **Procurar…** se quiser).
4. Escolha um "Modelo de projeto". Os modelos já trazem a resolução, a proporção e a taxa de quadros indicadas. Há modelos para vídeo horizontal (Full HD, 4K e cinema), vertical e quadrado, e modelos para plataformas como YouTube, Instagram, TikTok, WhatsApp e Facebook.
5. Se preferir, ajuste a resolução, a proporção, a taxa de quadros (FPS) e o perfil de cores manualmente.
6. Em "Armazenamento das mídias", escolha uma opção:
   - **Vincular mídias (arquivos ficam onde estão)**: o projeto guarda apenas o caminho dos arquivos. É o mais leve, mas os arquivos precisam continuar no mesmo lugar.
   - **Empacotar mídias no projeto (.med independente)**: as mídias são copiadas para dentro do projeto. Use se quiser levar o projeto para outro computador.
7. Clique em **Criar projeto**.

### Abrir um projeto existente

Há três maneiras:

- Na tela inicial, clique em **Abrir Projeto** e escolha o arquivo .med.
- Clique num projeto da lista **Projetos recentes**.
- Dê um duplo clique no arquivo .med no Explorador de Arquivos. O programa abre o projeto automaticamente.

## A tela de edição

Depois de criar ou abrir um projeto, a tela de edição aparece. Ela é dividida em áreas:

- **Menus** (no topo): **Arquivo**, **Editar**, **Visualizar**, **Projeto**, **Ferramentas** e **Ajuda**.
- **Barra de ações** (logo abaixo dos menus): botões para as ações mais usadas, como **Salvar**, **Desfazer**, **Refazer**, **Importar Mídia**, **Fila de renderização** e **Exportar**.
- **Biblioteca de mídias** (à esquerda): tem os arquivos importados e uma lista de seções, como Vídeos, Imagens, Áudios, Elementos Gráficos, Textos, Transições, Efeitos, Cor e IA.
- **Pré-visualização** (no centro, em cima): mostra o resultado do vídeo, com os botões de reproduzir, pausar e avançar quadro a quadro, o volume e a resolução da pré-visualização.
- **Propriedades** (à direita): mostra as informações e as configurações do item selecionado. Está dividida em abas: **Mídia**, **Vídeo**, **Texto**, **Composição**, **IA**, **Áudio**, **Efeitos**, **Cor** e **Histórico**.
- **Linha do tempo** (embaixo): onde você monta o vídeo. Cada linha horizontal é uma trilha. Há uma barra de ferramentas logo acima dela.

> Quando o nome do projeto aparece com a marca "● não salvo", existem alterações que ainda não foram gravadas. Use **Arquivo › Salvar** ou Ctrl+S.

### Menus

- **Arquivo**: novo projeto, abrir, importar mídia, importar sequência de imagens, salvar, salvar como, criar cópia de segurança, exportar, fila de renderização e voltar ao início.
- **Editar**: desfazer e refazer.
- **Visualizar**: alterna o tema claro e escuro.
- **Projeto**: empacotar mídias no projeto e localizar mídias ausentes.
- **Ferramentas**: mixer de áudio, editor de legendas, importar e exportar legendas, modelos de IA e configurações.
- **Ajuda**: **Manual do usuário** (F1), **Atalhos de teclado…**, **Informações do sistema…**, **Abrir pasta de logs**, **Licenças de terceiros** e **Sobre o Meu Editor de Vídeo…**.

O **Manual do usuário** abre este manual dentro do programa, sem internet. A lista à esquerda leva a cada assunto e Ctrl+F procura uma palavra. A janela pode ficar aberta enquanto você edita.

**Atalhos de teclado…** mostra os atalhos em uso, inclusive os que você mudou. **Informações do sistema…** mostra a versão, o Windows, o processador, a memória, a placa de vídeo, os modelos de IA e as pastas do programa; o botão **Copiar** leva tudo para a área de transferência, útil ao pedir ajuda.

## Importar mídia

Importar é trazer para o projeto os arquivos que você vai usar, como vídeos, fotos, músicas e gravações. Os arquivos originais não são alterados.

### Formatos aceitos

- **Vídeo**: MP4, M4V, MOV, MKV, WebM, AVI, WMV, FLV, MPG e MPEG, M2TS, MTS, TS, 3GP, OGV, MXF e VOB.
- **Áudio**: MP3, WAV, AAC, M4A, FLAC, OGG, OGA, Opus, WMA, AIFF, AIF, AC3 e AMR.
- **Imagem**: JPG e JPEG, PNG, BMP, TIF e TIFF, WebP, TGA, JFIF e SVG.
- **GIF animado**: GIF.
- **Sequência de imagens**: uma série de fotos numeradas, como foto_0001.png, foto_0002.png e assim por diante.

Arquivos de outros formatos são recusados, com a mensagem "Formato de arquivo não suportado.".

### Como importar

1. Clique em **Importar Mídia** na barra de ações, ou use **Arquivo › Importar mídia…** (atalho Ctrl+I).
2. Escolha um ou vários arquivos e confirme.
3. Acompanhe o progresso. A mensagem "Importando 3 de 10" mostra quantos arquivos já foram lidos.

Você também pode arrastar arquivos ou pastas do Explorador de Arquivos e soltá-los sobre a biblioteca de mídias. Quando o arquivo está sobre a área certa, aparece a indicação "Solte para importar".

Para uma sequência de imagens, use **Arquivo › Importar sequência de imagens…**. O programa reconhece a numeração automaticamente.

Cada mídia importada ganha uma miniatura. Ao selecionar uma mídia, a aba **Mídia** das propriedades mostra duração, resolução, taxa de quadros e outros dados do arquivo.

### Organizar a biblioteca

- Use a caixa **Pesquisar…** para encontrar uma mídia pelo nome.
- Clique com o botão direito numa mídia para **Renomear…**, **Definir categoria…**, **Remover categoria**, **Adicionar à linha do tempo** ou **Remover do projeto**.
- Remover uma mídia do projeto não apaga o arquivo do computador.

### Mídia ausente

Se um arquivo foi movido, renomeado ou apagado, o projeto mostra o aviso "Algumas mídias do projeto não foram encontradas" ao ser aberto. O aviso também aparece na barra de status como "Mídias ausentes", com a quantidade de mídias afetadas. Para resolver:

1. Clique em **Localizar…** para indicar o arquivo, ou em **Procurar em pasta…** para o programa procurar nas pastas e subpastas que você escolher.
2. Se não quiser localizar agora, clique em **Continuar sem as mídias**. Os trechos ficam marcados como ausentes e podem ser localizados depois.

Você pode abrir essa janela a qualquer momento em **Projeto › Localizar mídias ausentes…**.

Para evitar esse problema ao levar o projeto para outro computador, use **Projeto › Empacotar mídias no projeto…**. Ele copia as mídias para dentro do arquivo do projeto.

### Proxies (versões leves para edição)

Um proxy é uma cópia leve de um vídeo grande, em 540p, usada só para a pré-visualização ficar mais fluida. A exportação sempre usa o arquivo original.

- Para criar um proxy, clique com o botão direito no vídeo na biblioteca e escolha **Gerar proxy de edição (540p)**.
- Para apagar um proxy, escolha **Excluir proxy**.
- No monitor, a opção **Usar proxies na pré-visualização** liga ou desliga o uso dos proxies.
- Em **Ferramentas › Configurações…**, você pode pedir que os proxies sejam gerados automaticamente para vídeos acima de 1080p.

## Editar na linha do tempo

A linha do tempo é onde o vídeo é montado da esquerda para a direita. O **cursor** (uma linha vertical) mostra o instante que está sendo visto na pré-visualização. Os pedaços de mídia colocados nas trilhas são chamados de clipes.

### Colocar mídias na linha do tempo

- Arraste uma mídia da biblioteca para uma trilha.
- Ou selecione a mídia na biblioteca e clique em **Adicionar à linha do tempo**, na barra de ferramentas.

### Mover, cortar e dividir

- **Mover**: arraste o clipe para outro lugar da trilha ou para outra trilha do mesmo tipo.
- **Aparar o início ou o fim**: arraste a borda do clipe para encurtar ou alongar o trecho usado.
- **Dividir**: posicione o cursor no ponto desejado e clique em **Dividir** na barra de ferramentas, ou pressione S. O clipe é cortado em duas partes nesse ponto.
- **Excluir**: selecione o clipe e pressione Delete. Também é possível usar o botão **Excluir seleção**.
- **Excluir e fechar espaço**: apaga o clipe e puxa os clipes seguintes para preencher o vazio. Use Shift+Delete.
- **Copiar, recortar, colar e duplicar**: use Ctrl+C, Ctrl+X, Ctrl+V e Ctrl+D. Ao colar, o clipe vai para a posição do cursor.

### Ímã e edição em cascata

- **Ímã de alinhamento** (tecla N): quando ligado, o clipe "gruda" nas bordas de outros clipes e no cursor, o que facilita encaixar as partes sem deixar espaços. O botão fica na barra de ferramentas.
- **Edição em cascata (ripple)**: quando ligada, mover ou aparar um clipe empurra os clipes seguintes, sem deixar buracos.

### Trilhas

As trilhas são as linhas horizontais da linha do tempo. Para adicionar uma trilha, clique no botão **Adicionar trilha** e escolha o tipo:

- **Trilha de vídeo**
- **Trilha de áudio**
- **Trilha de imagem**
- **Trilha de texto**
- **Trilha de legenda**
- **Trilha de efeitos**

Em cada trilha, no cabeçalho à esquerda, há três botões:

- **Ocultar**: esconde a trilha na pré-visualização.
- **Mudo**: silencia a trilha de áudio.
- **Bloquear**: impede alterações na trilha, para evitar mexer no que já está pronto.

Clique com o botão direito no nome da trilha para **Renomear trilha…** ou **Remover trilha**.

### Agrupar e vincular

- **Agrupar** (Ctrl+G): une vários clipes para que se movam juntos. Use **Desagrupar** (Ctrl+Shift+G) para separar.
- **Vincular áudio e vídeo**: faz o áudio de um vídeo seguir o vídeo. Use **Desvincular áudio e vídeo** para separar.

### Velocidade do clipe

Clique com o botão direito no clipe e escolha **Velocidade…** para deixá-lo mais rápido ou mais lento. Na aba **Vídeo** das propriedades, a lista de velocidades vai de 0,25× (câmera lenta) a 8× (acelerado).

### Marcadores

Marcadores são lembretes na régua de tempo, úteis para anotar pontos importantes, como o início de uma fala.

- Posicione o cursor e clique em **Adicionar marcador** ou pressione M.
- Clique com o botão direito no marcador para **Renomear marcador…** ou **Remover marcador**.

### Zoom da linha do tempo

Use os botões de zoom na barra de ferramentas, ou os atalhos:

- Aproximar: Ctrl + tecla de mais (ou + do teclado numérico)
- Afastar: Ctrl + tecla de menos (ou - do teclado numérico)
- Ajustar à janela: Shift+Z

### Desfazer e refazer

- **Desfazer** (Ctrl+Z) e **Refazer** (Ctrl+Y ou Ctrl+Shift+Z) voltam e avançam as últimas ações.
- A aba **Histórico**, nas propriedades, mostra a lista de ações feitas. Clique numa delas para voltar o projeto àquele ponto.

### Navegar pelo vídeo

- Espaço: reproduz ou pausa.
- Setas para a esquerda e para a direita: voltam ou avançam um quadro.
- Shift com as setas: voltam ou avançam um segundo.
- Cima e baixo: vão para o corte anterior e para o próximo corte.
- Home e End: vão para o início e para o fim.

## Efeitos visuais, texto e transições

Para aplicar um efeito, selecione o clipe na linha do tempo e use as abas das propriedades. Você também pode usar as seções da biblioteca, como **Efeitos**, **Cor** e **Transições**: clique duas vezes num item para aplicá-lo no cursor ou ao clipe selecionado, ou use o botão **Aplicar ao clipe selecionado**.

### Aba Vídeo

Esta aba reúne as configurações de movimento e tempo do clipe:

- **Transformação**: posição X e Y, escala, rotação e opacidade. Ao lado de cada valor há um botão ◇ que cria um keyframe.
- **Keyframes**: um keyframe guarda um valor num instante específico. Quando há keyframes, o programa muda o valor gradualmente entre eles. Use **Keyframe anterior** e **Próximo keyframe** para navegar entre eles, e **Interpolação** para escolher a curva: Suave (entrada e saída), Linear, Acelerando, Desacelerando ou Manter (salto).
- **3D (perspectiva)**: giro X, giro Y e profundidade, para dar sensação de profundidade ao clipe. O botão **Virar cartão 3D** faz o clipe girar como um cartão. Não é um modelo 3D completo.
- **Animações prontas**: efeitos de movimento prontos para aplicar, como entrar pela esquerda, aparecer com zoom, Ken Burns (zoom lento), pulsar, girar, balançar, quicar e surgir e sumir.
- **Recorte**: corta as bordas do vídeo pela esquerda, pelo topo, pela direita e pela base.
- **Mistura**: modo de mistura com outras camadas, como Multiplicar, Tela, Sobrepor, Luz suave, Adicionar, Escurecer, Clarear e Diferença.
- **Fades**: **Entrada gradual** e **Saída gradual**, de 0 a 10 segundos, para o clipe surgir e desaparecer suavemente.
- **Velocidade**: velocidades predefinidas ou outra velocidade. Abaixo de 1×, a opção **Câmera lenta suavizada** mistura quadros vizinhos para reduzir os saltos.
- **Congelar quadro**, **Repetir trecho**, **Preencher quadro**, **Girar 90°** e **Espelhar** na horizontal e na vertical.
- **Transição de entrada**: aplica uma transição entre este clipe e o clipe que vem antes dele na mesma trilha. Para usar, os dois clipes precisam estar encostados. Você escolhe a transição e a **Duração da transição**.

As transições disponíveis são: Corte seco, Dissolver, Escurecer e clarear (fade), Zoom, Deslizar (para a esquerda, a direita, cima e baixo), Empurrar (para a esquerda e para a direita), Cortina (para a esquerda, a direita, cima e baixo), Círculo, Losango, Relógio e Persianas.

### Efeitos

A aba **Efeitos** tem duas partes. Em cima, os botões com o nome de cada efeito: clique num deles para adicionar o efeito ao clipe. Embaixo, a lista **Efeitos aplicados**, na ordem em que os efeitos são processados. Cada efeito da lista tem controles deslizantes para os ajustes, e os botões **Mover para cima**, **Mover para baixo** e **Remover efeito**. Dê um duplo clique num controle deslizante para voltar ao valor padrão.

Efeitos visuais disponíveis:

- **Desfoque**, **Nitidez**, **Vinheta**, **Preto e branco**, **Sépia**, **Negativo**, **Pixelizar**, **Granulado**, **Posterizar** e **Brilho suave**.
- **Chroma key**: remove um fundo verde, azul ou de outra cor escolhida. Tem tolerância, suavidade da transição, suavização das bordas, remoção do reflexo da cor e a opção de deixar o fundo transparente ou com cor sólida.
- **Refinar bordas**: contrai ou expande a borda da transparência, suaviza e limpa a cor das bordas. Use depois do chroma key ou de uma máscara.

A opção **Comparar antes/depois no monitor** mostra, numa metade do monitor, o vídeo sem efeitos e sem correção de cor, para você comparar.

### Cor

A aba **Cor** e a seção **Cor** da biblioteca têm as ferramentas de correção de cor:

- **Ajustes de cor**: exposição, brilho, contraste, saturação, temperatura, tonalidade (verde ou magenta), matiz, sombras e realces.
- **Balanço de branco**: indique a cor que deveria ser cinza neutro, e o programa corrige a dominante.
- **Curvas**: curvas de tons do vídeo inteiro e por canal (vermelho, verde e azul).
- **Rodas de cor**: ajuste separado das sombras, dos tons médios e dos realces.
- **LUT 3D (.cube)**: aplica uma tabela de cores no formato .cube, muito usada para dar um estilo ao vídeo. Escolha a intensidade com o controle deslizante.

### Máscaras, rastreamento e estabilização

Esses recursos ficam na aba **Composição**. Eles funcionam para vídeos, imagens, textos e elementos gráficos.

- **Máscaras**: crie uma forma com **+ Retângulo**, **+ Elipse** ou **+ Livre** (pontos, com Ctrl+clique no monitor). A máscara recorta o que aparece do clipe. Você pode somar, subtrair ou fazer a interseção de máscaras, inverter a máscara e suavizar o contorno.
- **Forma-chave**: com o botão **◇ Forma-chave**, a máscara muda de forma ao longo do tempo.
- **Rastreamento de movimento**: marque um objeto no monitor, clique em **Rastrear** e o programa acompanha esse objeto. Outro clipe pode então **Seguir** o movimento, com a opção **Seguir**. Use **Soltar** para desligar.
- **Estabilização**: **Analisar e estabilizar** mede o movimento da câmera e compensa os tremores. Ajuste a **Suavidade** e a **Ampliação** da imagem. Use **Remover estabilização** para desfazer.

### Elementos gráficos e formas

Na seção **Elementos Gráficos** da biblioteca, clique duas vezes numa forma para inseri-la no cursor. As formas disponíveis são: Retângulo, Retângulo arredondado, Círculo, Linha, Seta, Estrela, Hexágono, Balão de fala e Coração. Você pode ajustar o preenchimento e a cor do contorno na aba **Composição**.

Também é possível importar um gráfico vetorial pelo botão **Importar SVG…**.

### Textos e legendas

### Inserir textos

Na seção **Textos** da biblioteca, clique duas vezes num modelo para inseri-lo no cursor:

- **Texto simples**: texto em negrito com sombra suave.
- **Título**: título grande no centro, com entrada em zoom.
- **Tarja (lower third)**: nome e cargo no canto inferior esquerdo, com caixa e barra de destaque.
- **Créditos rolantes**: lista de créditos que sobe pela tela durante o clipe.
- **Legenda**: legenda na trilha de legendas, com caixa escura na base do quadro.

Selecione o texto na linha do tempo para editar. A aba **Texto** das propriedades tem:

- **Conteúdo**: o texto. Pressione Enter para quebrar a linha.
- **Fonte**: fontes do Windows ou fontes adicionadas ao programa com **+ Fonte** (arquivos .ttf ou .otf).
- **Negrito**, **Itálico** e **Sublinhado**.
- **Cores** do texto, do contorno, da caixa de fundo e da sombra. Use o formato #RRGGBB ou #AARRGGBB para definir a cor.
- **Alinhamento** e **Ponto de apoio**.
- **Animação de entrada** e **Animação de saída**: por exemplo, esmaecer, deslizar ou zoom.

A posição, a escala, a rotação e a opacidade do texto ficam na aba **Vídeo**.

### Legendas

Legendas são textos com horário, organizados na trilha de legenda. Para trabalhar com elas:

- **Ferramentas › Importar legendas (SRT/VTT)…**: traz um arquivo de legendas pronto.
- **Ferramentas › Exportar legendas (SRT/VTT)…**: salva as legendas do projeto como arquivo SRT ou VTT.
- **Ferramentas › Editor de legendas**: abre uma janela para ver e editar todas as legendas. É possível adicionar, remover e editar o texto e os horários, ir para uma legenda específica e deslocar todas de uma vez, em segundos.
- **Gerar legendas**: cria legendas automaticamente a partir da fala. Veja a seção Inteligência artificial.

Ao exportar o vídeo, é possível escolher entre salvar um arquivo de legendas ao lado do vídeo ou desenhar as legendas no próprio vídeo (legendas queimadas).

## Áudio

### Mixer de áudio

Abra o mixer em **Ferramentas › Mixer de áudio** ou pelo botão da barra de ferramentas. Ele mostra uma coluna para cada trilha de áudio, com:

- **Volume** (fader) e **panorâmica** (esquerda e direita). Clique duas vezes no fader ou na panorâmica para voltar ao padrão.
- **M** (mudo): silencia a trilha.
- **S** (solo): deixa só as trilhas com solo tocando.
- Medidores de nível, que se movem durante a reprodução.

### Aba Áudio do clipe

Selecione um clipe de áudio, ou um vídeo com som, e use a aba **Áudio**:

- **Volume**: 100% é o volume original. Com o botão ◇, você cria um keyframe de volume. Assim é possível abaixar a música durante uma fala, por exemplo.
- **Entrada gradual** e **Saída gradual**: fades de 0 a 10 segundos, para o som começar e terminar suavemente.
- **Normalizar loudness**: ajusta o volume para um alvo de loudness (padrão EBU R128), sem passar de um pico seguro. Escolha o **Alvo de loudness** antes de aplicar.
- **Remover silêncios**: corta os trechos silenciosos do clipe. Ajuste o **Limiar**, abaixo do qual o som é considerado silêncio, e a **Duração mínima** do silêncio.
- **Sincronizar pelo áudio**: se você gravou o mesmo momento com duas câmeras ou com um gravador de som, selecione os dois clipes e clique em **Sincronizar**. O programa alinha os dois sons. O clipe da trilha mais acima fica parado, e o outro é movido.
- **Manter o tom na velocidade**: quando o clipe é acelerado ou desacelerado, a voz continua com tom natural.

### Efeitos de áudio

Na mesma aba, em **Adicionar efeito de áudio**, estão os efeitos:

- **Equalizador**: graves, médios e agudos, com corte de graves.
- **Compressor**: aproxima os trechos fortes dos fracos, com limiar, razão, ataque, liberação e ganho de compensação.
- **Redução de ruído**: diminui chiados e ruídos constantes, como ventilador ou ar-condicionado. Tem intensidade e sensibilidade.
- **Eco**: repetições do som com atraso.
- **Reverberação**: simula o som de uma sala.
- **Telefone / rádio**: som de telefone ou rádio antigo.
- **Distorção**: saturação do som.

Os efeitos são aplicados na ordem em que aparecem na lista **Efeitos de áudio aplicados**.

## Inteligência artificial

Os recursos de inteligência artificial do programa rodam somente no seu computador. Nenhum vídeo, áudio, imagem ou texto é enviado para a internet.

Os recursos ficam na aba **IA** das propriedades, quando você seleciona um clipe na linha do tempo. A seção **IA** da biblioteca resume os recursos e tem o botão **Gerenciar modelos de IA…**, que abre o gerenciador de modelos.

### Modelos necessários

Cada recurso usa um modelo de IA. O modelo do **Remover fundo** já vem com o programa. Os demais precisam ser instalados, uma única vez, pela ferramenta **Baixar modelos de IA**. Quando o modelo de um recurso não está instalado, o botão fica desativado e aparece a mensagem "Indisponível — instale o modelo em Ferramentas › Modelos de IA.".

| Recurso | Modelo | Precisa instalar | Processa em |
|---|---|---|---|
| Remover fundo | MODNet | Não, já vem com o programa | CPU ou GPU |
| Calcular máscaras em segundo plano | MODNet | Não, já vem com o programa | CPU ou GPU |
| Rastrear pessoa | MODNet | Não, já vem com o programa | CPU ou GPU |
| Gerar legendas | Whisper large-v3 turbo | Sim, cerca de 550 MB | Somente CPU |
| Isolar voz | GTCRN | Sim, cerca de 0,5 MB | Somente CPU |
| Reduzir ruído com IA | GTCRN | Sim, cerca de 0,5 MB | Somente CPU |
| Remover trechos sem fala | Silero VAD 5 | Sim, cerca de 2,3 MB | Somente CPU |
| Detectar cenas | TransNetV2 | Sim, cerca de 31 MB | CPU ou GPU |
| Melhorar imagem | Swin2SR leve 2× | Sim, cerca de 8 MB | CPU ou GPU |

A detecção de cenas funciona mesmo sem o modelo TransNetV2, comparando as imagens do vídeo. Nesse caso, a precisão é menor.

O reconhecimento de fala (legendas) usa o Silero VAD para encontrar os trechos com fala. Ao instalar o Whisper, o Silero VAD é instalado junto, automaticamente.

> Os modelos da tabela ocupam espaço no disco e também memória ao serem usados. O Whisper, por exemplo, precisa de cerca de 1,5 GB de memória livre.

### Remover fundo

Separa a pessoa do fundo e deixa o fundo transparente, ou o substitui por outra coisa. Use na aba **IA**, com **Remover fundo com IA**.

- **Novo fundo**: escolha **Transparente**, **Cor sólida**, **Desfoque** ou **Imagem ou vídeo da biblioteca**. Neste último caso, escolha o arquivo em **Mídia do fundo**.
- **Manter**: deixe a pessoa (primeiro plano) ou o fundo, para remover a pessoa.
- **Ajustes**: o limiar da máscara, a contração ou expansão da borda, a suavização da borda e a suavização entre quadros ficam na aba **Efeitos**, em **Efeitos aplicados**. A suavização entre quadros reduz o tremor da borda de um quadro para o outro.

O modelo foi treinado principalmente para retratos. Em cenas com objetos parecidos com pessoas, ele pode manter partes do fundo.

Use **Calcular máscaras em segundo plano** para processar o clipe inteiro antes. Assim a reprodução e a exportação ficam mais rápidas. O resultado fica guardado no cache.

Use **Rastrear pessoa com IA** para criar um rastreamento da pessoa. Depois, na aba **Composição**, você pode vincular textos ou elementos gráficos a ela.

### Gerar legendas

Cria legendas automaticamente a partir da fala do clipe.

1. Selecione o clipe e vá à aba **IA**.
2. Em **Idioma da fala**, escolha Português, Inglês, Espanhol ou o idioma automático.
3. Clique em **Gerar legendas com IA**.
4. As legendas aparecem na trilha de legenda. Revise-as no **Editor de legendas**.

O reconhecimento roda no processador, e leva cerca de 4 minutos para um vídeo de 10 minutos. Acompanhe o andamento na barra de progresso da aba.

### Isolar voz e reduzir ruído

- **Isolar voz com IA**: separa a voz do ambiente. O programa cria duas trilhas novas, uma com a voz e outra com o ambiente.
- **Reduzir ruído com IA**: diminui o ruído de fundo, com a **Intensidade da redução de ruído** escolhida. Útil para gravações com chiado ou zumbido.

Essas operações são feitas com o áudio do clipe, do jeito que ele soa na linha do tempo. A marcação **Silenciar o áudio original** evita ouvir o som antigo junto com o novo.

### Remover trechos sem fala

Encontra os trechos sem fala e os remove do clipe. Use **Pausa mínima** para definir a duração mínima de pausa que será cortada, e depois clique em **Remover trechos sem fala com IA**. Este recurso precisa do modelo Silero VAD instalado; sem ele, o botão fica indisponível. Para cortar silêncios sem IA, use a aba **Áudio** (**Remover silêncios**).

### Detectar cenas

Procura as mudanças de cena do vídeo e cria um marcador em cada uma.

1. Selecione o clipe e vá à aba **IA**.
2. Ajuste a **Sensibilidade** das cenas. Quanto maior, mais cenas são encontradas.
3. Marque **Dividir o clipe em cada cena**, se quiser que o clipe seja cortado em cada cena.
4. Clique em **Detectar cenas**.

### Melhorar imagem

Amplia uma foto em 2× reconstruindo os detalhes. Selecione a imagem, vá à aba **IA** e clique em **Melhorar imagem com IA**. O clipe passa a usar a nova imagem. Para voltar à original, use **Desfazer**.

### Cancelar um processamento

Enquanto um recurso de IA está rodando, aparece uma barra de progresso. Clique em **Cancelar processamento de IA** para interromper.

## Exportar o vídeo

Exportar é gerar o arquivo final do vídeo. Clique em **Exportar** na barra de ações, ou use **Arquivo › Exportar…** (atalho Ctrl+E).

### Modelos de exportação

Na lista à esquerda da janela de exportação, escolha um modelo. Cada modelo já traz a configuração adequada para uma plataforma:

- YouTube
- YouTube Shorts
- Instagram Feed
- Instagram Reels
- TikTok
- WhatsApp Status
- WhatsApp compartilhamento
- Facebook Feed
- Facebook Reels
- Apresentações
- Computadores e notebooks
- Exportação personalizada

Ajuste os parâmetros e, se quiser, clique em **Salvar como novo…** para guardar sua configuração. Os modelos próprios aparecem com o selo "Meu modelo" e podem ser editados, duplicados ou excluídos. Os modelos padrão não podem ser apagados, mas você pode usar **Restaurar padrão** para voltar a configuração original de um modelo, ou **Restaurar todos**.

### Parâmetros

- **Formato do arquivo**: MP4, MOV (QuickTime), MKV, WebM, AVI, MPEG-2 (.mpg) e GIF animado. Nem todo formato aceita todos os codecs. Se a combinação não for possível, a janela avisa.
- **Codificador**: **Automático (placa de vídeo quando disponível)**, **Placa de vídeo** ou **Software (mais compatível)**. O modo Software é o mais seguro para arquivos que precisam abrir em qualquer aparelho.
- **Codec de vídeo**: H.264 (AVC), H.265 (HEVC), AV1, VP9, Apple ProRes, FFV1, QuickTime Animation, Motion JPEG, MPEG-2 ou GIF. Para a maioria dos casos, escolha H.264.
- **Perfil**: Padrão, High, Main ou Baseline do codec. Mantenha Padrão se não souber.
- **Controle de taxa**: **Taxa variável (VBR)**, **Taxa constante (CBR)** ou **Qualidade constante**. Com a qualidade constante, use valores entre 18 e 23, onde números menores significam melhor qualidade.
- **Taxa de bits do vídeo (kbps)**: quanto maior, maior a qualidade e o tamanho do arquivo. Para a internet, use o valor sugerido pelo modelo.
- **Resolução**: marque **Usar a resolução do projeto** ou informe a largura e a altura em pixels.
- **Taxa de quadros**: **Do projeto**, ou uma das taxas padrão (23,976, 24, 25, 29,97, 30, 50, 59,94 e 60 FPS).
- **Codec de áudio**, **Taxa de amostragem** (44,1 kHz, 48 kHz ou 96 kHz), **Canais** (Mono, Estéreo ou 5.1) e **Taxa de bits do áudio (kbps)**.
- **Exportar transparência (canal alfa)**: mantém o fundo transparente. Use com ProRes 4444, QuickTime Animation, FFV1, VP9 ou GIF.

### Intervalo e divisão do arquivo

- **Intervalo**: escolha **Sequência inteira** para exportar o projeto todo, ou **Intervalo personalizado** para exportar só um trecho. Defina o **Início** e o **Fim**, ou use **Início no cursor**.
- **Dividir em arquivos**: divide a exportação em partes de até determinada duração em segundos, ou com um limite de tamanho em MB. Útil para plataformas com limite de tamanho.

### Legendas na exportação

Em **Legendas**, escolha **Nenhum**, **SubRip (.srt)** ou **WebVTT (.vtt)** para salvar um arquivo de legendas ao lado do vídeo. Marque **Desenhar as legendas no vídeo (legendas queimadas)** para que as legendas fiquem gravadas na imagem.

### Destino e início

1. Escolha a **Pasta de destino** e o **Nome do arquivo**.
2. Marque **Renderizar em segundo plano**, se quiser continuar editando enquanto o vídeo é gerado.
3. Clique em **Exportar** para iniciar.

### Fila de renderização

Cada exportação entra na fila de renderização. Abra a fila com o botão **Fila de renderização** ou em **Arquivo › Fila de renderização…**. Para cada item, a fila mostra o estado, o progresso, a velocidade e o tempo restante:

- **Na fila**: aguardando a vez.
- **Renderizando**: em andamento.
- **Concluída**: pronto. Use **Abrir arquivo exportado** ou **Mostrar na pasta**.
- **Interrompida**: pode ser continuada com **Retomar**. Ao retomar, o programa aproveita os blocos já gerados.
- **Falhou**: a mensagem explica o motivo.
- **Cancelada**: interrompida pelo usuário.

Use **Cancelar exportação** para parar um item, **Remover da fila** para tirá-lo da lista e **Limpar concluídas** para esvaziar os itens já prontos.

## Salvar, salvamento automático e recuperação

### Salvar o projeto

- **Arquivo › Salvar** (Ctrl+S): grava as alterações no arquivo do projeto.
- **Arquivo › Salvar como…** (Ctrl+Shift+S): grava uma cópia do projeto com outro nome ou em outra pasta.
- **Arquivo › Criar cópia de segurança**: cria uma cópia de segurança do projeto no momento atual. O programa guarda até 10 cópias de cada projeto, na pasta de cópias de segurança do aplicativo.

O arquivo do projeto tem a extensão .med. Os arquivos das mídias originais não são alterados pelo programa.

### Salvamento automático

Por padrão, o programa salva uma cópia de recuperação a cada 5 minutos. Você pode desligar essa opção, ou mudar o intervalo, em **Ferramentas › Configurações…**, na aba **Geral**.

A cópia de recuperação não substitui o arquivo do projeto. Ela só é oferecida quando o programa é aberto depois de um fechamento inesperado, como queda de energia ou travamento.

### Recuperar um projeto

Se o programa foi fechado sem salvar, a tela inicial mostra a faixa "Há um projeto que pode ser recuperado", com dois botões:

- **Recuperar**: abre o projeto com as últimas alterações.
- **Descartar**: apaga as alterações recuperáveis. Essa ação não pode ser desfeita.

> Recomenda-se salvar manualmente com Ctrl+S com frequência, mesmo com o salvamento automático ligado.

## Configurações e desempenho

Abra as configurações em **Ferramentas › Configurações…** (atalho Ctrl+,) ou no botão **Configurações** da tela inicial. As configurações são divididas em quatro abas. Nada é gravado até você clicar em **Salvar**.

### Aba Geral

- **Tema**: **Escuro**, **Claro** ou **Igual ao Windows**. Você também pode alternar pelo menu **Visualizar › Alternar tema claro/escuro**.
- **Abrir o último projeto ao iniciar**.
- **Pastas padrão**: a pasta dos projetos e a pasta das exportações.
- **Salvamento automático**: ligar ou desligar a cópia de recuperação e definir o intervalo.

### Aba Desempenho

- **Hardware detectado**: mostra o processador, a memória, as placas de vídeo, os discos e se a placa de vídeo pode decodificar e codificar vídeo. O programa também indica as limitações do computador e uma recomendação de uso.
- **Pré-visualização**: a **Qualidade padrão da pré-visualização** pode ser **Automática**, **Completa**, **1/2 da resolução** ou **1/4 da resolução**. Na opção Automática, o programa escolhe conforme o computador e o projeto, e reduz a resolução durante a reprodução se o computador não acompanhar. A **Memória para quadros da pré-visualização** define quanto espaço usar para guardar quadros prontos.
- **Proxies**: **Usar proxies na pré-visualização quando existirem** e **Gerar proxies automaticamente para vídeos acima de 1080p**.
- **Aceleração**: **Usar a placa de vídeo para decodificar e codificar vídeo** e **Renderizar a fila de exportação em segundo plano**.
- **Inteligência artificial**: o **Dispositivo da IA** pode ser **GPU (DirectML), com recuo para a CPU**, ou **Somente CPU**. Com a opção de GPU, se a placa ou o driver falhar, o modelo é carregado no processador automaticamente. O botão **Gerenciar modelos…** abre a janela de modelos de IA.

### Aba Cache

O cache guarda miniaturas, formas de onda, proxies e máscaras de IA, para que não precisem ser recalculados. Nesta aba você vê quanto espaço cada área usa, define o **Limite do cache** em GB (padrão de 4 GB), e pode usar **Aplicar o limite agora** ou **Limpar cache**.

Quando o limite é ultrapassado, os itens usados há mais tempo são apagados. Os projetos e as mídias originais nunca são apagados pela limpeza do cache.

### Aba Atalhos

Esta aba mostra todas as ações do programa, agrupadas por categoria. Para trocar um atalho:

1. Clique no campo do atalho.
2. Pressione a nova combinação de teclas.
3. Clique em **Salvar**.

Se a combinação já estiver em uso por outra ação, o programa avisa e não deixa salvar. Use **Padrão** numa ação para voltar ao atalho original, ou **Restaurar todos os padrões** para desfazer todas as mudanças. Os menus sempre mostram o atalho em uso.

## Atalhos de teclado

Estes são os atalhos padrão. Você pode mudar qualquer um deles em **Ferramentas › Configurações… › Atalhos**.

| Ação | Atalho |
|---|---|
| Novo projeto | Ctrl+N |
| Abrir projeto | Ctrl+O |
| Salvar | Ctrl+S |
| Salvar como | Ctrl+Shift+S |
| Importar mídia | Ctrl+I |
| Exportar | Ctrl+E |
| Desfazer | Ctrl+Z |
| Refazer | Ctrl+Y ou Ctrl+Shift+Z |
| Configurações | Ctrl+, |
| Manual do usuário | F1 |
| Reproduzir / pausar | Espaço |
| Tela cheia | F11 (Esc ou F11 para sair) |
| Quadro anterior | ← |
| Próximo quadro | → |
| Voltar 1 segundo | Shift+← |
| Avançar 1 segundo | Shift+→ |
| Ir para o início | Home |
| Ir para o fim | End |
| Corte anterior | ↑ |
| Próximo corte | ↓ |
| Dividir clipe no cursor | S ou Ctrl+K |
| Excluir | Delete ou Backspace |
| Excluir e fechar o espaço | Shift+Delete |
| Copiar | Ctrl+C |
| Recortar | Ctrl+X |
| Colar | Ctrl+V |
| Duplicar | Ctrl+D |
| Selecionar tudo | Ctrl+A |
| Agrupar | Ctrl+G |
| Desagrupar | Ctrl+Shift+G |
| Adicionar marcador | M |
| Ligar/desligar ímã | N |
| Aproximar a linha do tempo | Ctrl++ (tecla de mais) |
| Afastar a linha do tempo | Ctrl+- (tecla de menos) |
| Ajustar à janela | Shift+Z |

## Privacidade

O Meu Editor de Vídeo foi feito para funcionar no seu computador:

- Seus vídeos, fotos, áudios, textos e projetos são processados somente no seu computador.
- Os recursos de inteligência artificial também rodam no seu computador. Nenhum conteúdo é enviado para a internet.
- O programa não coleta dados de uso e não envia telemetria.
- O programa não se atualiza sozinho pela internet.

Existe uma única exceção. Quando você clica em **Instalar** na ferramenta **Baixar modelos de IA**, o programa baixa os arquivos dos modelos de IA dos sites oficiais dos seus autores, que são os endereços huggingface.co e github.com. Só os modelos escolhidos são baixados, e nenhum dado do seu projeto é enviado. Depois de instalados, os modelos funcionam sem internet. Você também pode instalar pacotes de modelo já baixados, com **Instalar pacote…**, sem usar a internet.

Para ver as licenças e os componentes de terceiros usados, abra **Ajuda › Sobre o Meu Editor de Vídeo…**. O texto completo das licenças fica em **Ajuda › Licenças de terceiros**.

## Solução de problemas

### O recurso de IA está "Indisponível"

O modelo do recurso ainda não foi instalado. Vá em **Ferramentas › Modelos de IA…**, clique em **Baixar modelos…**, escolha os modelos e clique em **Instalar**. A ferramenta instala primeiro as dependências, se houver. Para o recurso Remover fundo, nenhuma instalação é necessária.

### A instalação de um modelo foi interrompida

Abra **Baixar modelos…** de novo e clique em **Instalar**. O download continua de onde parou. Se o arquivo baixado estiver incompleto ou alterado, ele é descartado e baixado outra vez.

### A pré-visualização está travando ou lenta

- Na barra do monitor, em **Resolução da pré-visualização**, escolha **1/2** ou **1/4**.
- Ative o uso de proxies, criando proxies dos vídeos grandes com **Gerar proxy de edição (540p)**.
- Feche outros programas pesados enquanto edita.
- Em **Configurações › Desempenho**, confira se a aceleração por placa de vídeo está ligada.

A exportação sempre usa a resolução original, por isso um vídeo pode travar na pré-visualização e ainda assim ser exportado corretamente.

### Uma mídia aparece como ausente

O arquivo foi movido, renomeado ou apagado. Use **Projeto › Localizar mídias ausentes…** para indicar o novo caminho. Para evitar isso, use **Empacotar mídias no projeto** antes de mover o projeto.

### "Formato de arquivo não suportado"

O programa não lê esse formato. Converta o arquivo para um formato comum, como MP4 (vídeo) ou MP3 e WAV (áudio), com outro programa de conversão, e importe de novo.

### A exportação falhou

- Leia a mensagem na fila de renderização. Ela indica o motivo.
- Se aparecer a mensagem de codec incompatível com o formato, escolha outro codec ou outro formato.
- Se o erro envolver a placa de vídeo, escolha o codificador **Software (mais compatível)** e exporte de novo.
- Se o arquivo não abrir em outros aparelhos, use o modelo **Computadores e notebooks** ou o formato MP4 com H.264.

### Perdi alterações depois de um travamento

Ao abrir o programa, verifique a faixa de recuperação na tela inicial e clique em **Recuperar**. Se ela não aparecer, procure a cópia de segurança do projeto, que fica na pasta de cópias de segurança do aplicativo, dentro de `%LOCALAPPDATA%\MeuEditorDeVideo\backups`.

### O programa não abre ou apresenta erros

Os registros de funcionamento (logs) ficam na pasta `%LOCALAPPDATA%\MeuEditorDeVideo\logs`. Para abri-la, use **Ajuda › Abrir pasta de logs** (ou pressione Windows + R, cole esse caminho e pressione Enter). Ao pedir ajuda, envie o arquivo de registro mais recente junto com o texto de **Ajuda › Informações do sistema…**. Os registros não contêm o conteúdo dos seus vídeos.

### Dúvidas sobre a versão instalada

Abra **Ajuda › Sobre o Meu Editor de Vídeo…** para ver a versão do programa, a licença, as informações de privacidade e a lista de componentes e modelos de IA usados.
