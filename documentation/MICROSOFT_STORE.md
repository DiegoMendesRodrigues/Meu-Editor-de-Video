# Publicação na Microsoft Store

Este guia leva o Meu Editor de Vídeo do repositório até a Microsoft Store pelo **Partner Center**
(<https://partner.microsoft.com/dashboard>), o site de desenvolvedores da Microsoft.

O aplicativo é enviado como **pacote MSIX**. A Microsoft assina o pacote na publicação, então **não é preciso comprar
certificado de assinatura de código**. Quem instala pela Store não vê o aviso de "editor desconhecido" do SmartScreen.

## Arquivos

| Arquivo | Para quê |
|---|---|
| `scripts/build-msix.ps1` | Gera o pacote `artifacts/store/MeuEditorDeVideo_<versão>_x64.msix`, os ícones e os logotipos da listagem. |
| `installer/Store/Package.appxmanifest` | Manifesto do pacote: identidade, Windows 11, associação de arquivos `.med`, capacidades e dependência do runtime do Visual C++. |
| `installer/Store/store.json` | Identidade reservada no Partner Center (preencher no passo 3). |
| `installer/Store/LISTAGEM_*.md` (`PT-BR`, `EN-US`, `ES-ES`, `PT-PT`, `FR-FR`, `DE-DE`) | Textos da página da loja nos seis idiomas do programa: descrição, novidades, recursos, palavras-chave, requisitos e legendas das capturas. |
| `installer/Store/capturas/<idioma>/` (`pt-BR`, `en-US`, `es-ES`, `pt-PT`, `fr-FR`, `de-DE`) | Capturas de tela (1938 × 1038) para cada listagem, com o programa no idioma dela. |
| `POLITICA_DE_PRIVACIDADE.md` | Política de privacidade (precisa estar publicada num endereço público). |

## 1. Conta de desenvolvedor

1. Acesse o Partner Center e inscreva-se no programa **Aplicativos e jogos** (Windows & Xbox).
2. Escolha conta **Individual** (pessoa física; a inscrição de pessoa física deixou de ser paga) ou **Empresa**
   (exige validação da empresa).
3. Conclua a verificação de identidade.

## 2. Reservar o nome

1. **Aplicativos e jogos › Novo produto › Aplicativo MSIX ou PWA**.
2. Reserve o nome **Meu Editor de Vídeo**. Se já estiver em uso, reserve outro e atualize `displayName` em
   `installer/Store/store.json`.

## 3. Copiar a identidade do produto

Em **(o aplicativo) › Gerenciamento de produto › Identidade do produto**, copie os três valores para
`installer/Store/store.json`:

| Partner Center | `store.json` |
|---|---|
| `Package/Identity/Name` | `identityName` |
| `Package/Identity/Publisher` (começa com `CN=`) | `publisher` |
| `Package/Properties/PublisherDisplayName` | `publisherDisplayName` |

Esses valores não são segredo e podem ser commitados.

## 4. Gerar o pacote

Pré-requisitos: .NET SDK 10, Windows SDK (traz `makeappx`, `makepri` e `signtool`) e as dependências baixadas.

```powershell
pwsh scripts/fetch-deps.ps1
pwsh scripts/build-msix.ps1
```

O script publica o aplicativo (self-contained, x64), gera os ícones em todas as escalas a partir de `Imagens/Ícone`,
preenche o manifesto e cria `artifacts/store/MeuEditorDeVideo_<versão>_x64.msix`.

- A versão vem de `Directory.Build.props` e vira `1.4.0.0`. A Store exige que a quarta parte seja `0`.
- **A cada nova submissão, aumente `<Version>`** em `Directory.Build.props`: a Store recusa pacote com versão igual ou
  menor que a já publicada.
- Sem a identidade preenchida, o script para com uma mensagem (exceto com `-TestSign`).

## 5. Testar o pacote no seu computador (recomendado)

O pacote para a Store não é assinado e não instala direto. Para testar antes de enviar, gere uma versão assinada com
certificado de teste:

```powershell
pwsh scripts/build-msix.ps1 -TestSign
```

Depois, num PowerShell **como administrador**, rode os dois comandos que o script mostra no final:

```powershell
Import-Certificate -FilePath 'artifacts\store\certificado-de-teste.cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path 'artifacts\store\MeuEditorDeVideo_1.4.0.0_x64.msix'
```

Confira:

- o aplicativo abre pelo menu Iniciar e aparece com o ícone certo na barra de tarefas;
- duplo clique num arquivo `.med` abre o projeto;
- importar, pré-visualizar, exportar e remover fundo com IA funcionam;
- **Ferramentas › Baixar modelos de IA** instala um modelo pequeno (ex.: Silero VAD).

Valide também com o **Windows App Certification Kit** (vem com o Windows SDK), como administrador:

```powershell
& "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe" test -appxpackagepath "<caminho do .msix>" -reportoutputpath "$env:TEMP\wack.xml"
```

Para remover o teste: `Get-AppxPackage *MeuEditorDeVideo* | Remove-AppxPackage`. Depois, apague o certificado de
teste em `certlm.msc › Pessoas Confiáveis` e `certmgr.msc › Pessoal`.

> Um pacote assinado com o certificado de teste **não pode ser enviado** à Store. Para enviar, gere de novo sem
> `-TestSign`.

### Diferenças em relação ao instalador MSI

- O Windows instala o pacote numa pasta protegida (`C:\Program Files\WindowsApps`). O aplicativo só lê arquivos de
  lá. Configurações, cache, modelos de IA e cópias de recuperação continuam em `%APPDATA%` e `%LOCALAPPDATA%`, mas o
  Windows guarda essas pastas separadas por pacote. Por isso, a versão da Store não enxerga as configurações da versão
  instalada pelo MSI, e vice-versa.
- O runtime do Visual C++ vem como dependência do pacote (`Microsoft.VCLibs.140.00.UWPDesktop`) e a Store o instala
  automaticamente.
- As atualizações chegam pela própria Store.

## 6. Publicar a política de privacidade

A Store exige uma **URL de política de privacidade**, porque o pacote declara acesso à internet (usado só pelo
downloader de modelos). Publique `POLITICA_DE_PRIVACIDADE.md` num endereço público. Por exemplo:

- num repositório público no GitHub (o link do arquivo serve), que também atende à exigência da GPL de oferecer o
  código-fonte; ou
- numa página do seu site.

Antes de publicar, preencha o contato no fim do arquivo.

## 7. Criar a submissão

Em **(o aplicativo) › Iniciar seu envio**:

### Preço e disponibilidade

- Mercados: todos. O programa e a página da loja existem em seis idiomas (português do Brasil, português de Portugal,
  inglês, espanhol, francês e alemão); os demais mercados veem a listagem em inglês.
- Preço: gratuito ou pago (a GPL permite cobrar, desde que o código-fonte continue disponível).

### Propriedades

- Categoria: **Foto e vídeo** (subcategoria: edição de vídeo, se disponível).
- Configurações de privacidade: **Sim, o produto acessa, coleta ou transmite informações pessoais?** → **Não**.
  Mesmo assim, informe a URL da política de privacidade.
- Requisitos do sistema: conforme `installer/Store/LISTAGEM_PT-BR.md`.

### Classificação etária

Responda ao questionário da IARC: o aplicativo não tem violência, conteúdo sexual, linguagem imprópria, compras,
interação ou compartilhamento entre usuários, nem localização. O resultado esperado é **Livre / 3+**.

### Pacotes

Envie `artifacts/store/MeuEditorDeVideo_<versão>_x64.msix`. Família de dispositivos: só **Desktop**.

### Listagens da Store

O pacote declara seis idiomas (`<Resources>` do manifesto), e a página da loja existe nos mesmos seis:

1. **Português (Brasil)**: textos de `installer/Store/LISTAGEM_PT-BR.md` e capturas de `installer/Store/capturas/pt-BR/`.
2. **Inglês (Estados Unidos)**: em **Gerenciar idiomas adicionais**, acrescente o idioma; depois use
   `installer/Store/LISTAGEM_EN-US.md` e `installer/Store/capturas/en-US/`.
3. **Espanhol (Espanha)**: do mesmo jeito, com `installer/Store/LISTAGEM_ES-ES.md` e `installer/Store/capturas/es-ES/`.
   O texto é neutro e pode ser repetido em variantes como Espanhol (México).
4. **Português (Portugal)**: `installer/Store/LISTAGEM_PT-PT.md` e `installer/Store/capturas/pt-PT/`.
5. **Francês (França)**: `installer/Store/LISTAGEM_FR-FR.md` e `installer/Store/capturas/fr-FR/`.
6. **Alemão (Alemanha)**: `installer/Store/LISTAGEM_DE-DE.md` e `installer/Store/capturas/de-DE/`.

Outros idiomas e regiões sem listagem própria mostram a listagem em inglês.

### Opções de envio: notas para a certificação

O pacote declara a capacidade restrita `runFullTrust`, e o Partner Center pede uma justificativa. Use:

```text
O Meu Editor de Vídeo é um aplicativo de área de trabalho Win32 (Avalonia/.NET 10) empacotado em MSIX. A capacidade runFullTrust é necessária para executar o aplicativo de desktop, que usa FFmpeg para decodificar e codificar vídeo, Direct3D/DirectML para aceleração por GPU e WASAPI para áudio.

Não é necessário login nem conta. Na primeira execução aparece a janela de boas-vindas: preencha o nome, escolha idioma e tema e clique em "Salvar as preferências". Depois o aplicativo abre direto no editor, com um projeto sem título. Para testar: importe um vídeo (Importar Mídia), adicione-o à linha do tempo e clique em "Exportar". A interface pode ser trocada para inglês, espanhol, francês, alemão ou português de Portugal em Ferramentas › Configurações… › Geral (o programa reinicia no novo idioma).

O aplicativo só usa a internet quando o usuário abre Ferramentas › Baixar modelos de IA e clica em Instalar: ele baixa modelos de IA (arquivos de dados ONNX/GGML, não executáveis) de endereços HTTPS fixos em huggingface.co e github.com, conferindo o SHA-256 de cada arquivo. Ajuda › Verificar atualizações (e uma verificação a cada 30 dias, que o usuário pode desligar) apenas consulta o número da versão mais recente. Nenhum dado do usuário é enviado.
```

Clique em **Enviar para a Store**. A certificação costuma levar de algumas horas a três dias úteis.

## 8. Publicar uma nova versão

O aplicativo já existe na Store; cada versão nova é uma **nova submissão** do mesmo produto. A identidade
(`store.json`) não muda.

### No repositório

1. Aumente `<Version>` em `Directory.Build.props`. A Store recusa pacote com versão igual ou menor que a já publicada
   (a versão 1.3.0 foi a anterior à 1.4.0).
2. Atualize a seção "O que há de novo nesta versão" dos seis `installer/Store/LISTAGEM_*.md`, cada um no seu idioma
   (até 1.500 caracteres).
3. Se a interface mudou bastante, refaça as capturas de cada pasta de `installer/Store/capturas/` (PNG de 1938 × 1038,
   com o programa no idioma da pasta).
4. Feche o Meu Editor de Vídeo, se estiver aberto (o build não consegue substituir os arquivos em uso), e gere o
   pacote:

   ```powershell
   pwsh scripts/build-msix.ps1
   ```

   O resultado é `artifacts/store/MeuEditorDeVideo_<versão>_x64.msix`. O script mostra o SHA-256 e o tamanho.
5. Opcional, mas recomendado: teste o pacote como no passo 5 (`-TestSign`). Depois do teste, rode o script de novo
   **sem** `-TestSign`, porque o pacote assinado com o certificado de teste não pode ser enviado.
6. Faça o commit e o push das listagens e da versão.

### No Partner Center

1. Abra <https://partner.microsoft.com/dashboard> › **Aplicativos e jogos** › **Meu Editor de Vídeo**.
2. Na **Visão geral do aplicativo**, clique em **Atualizar** ao lado da última submissão publicada. O Partner Center
   cria uma cópia da submissão anterior, com tudo preenchido.
3. **Pacotes**: arraste o `.msix` novo para a área de envio e espere a validação terminar. Remova o pacote da versão
   anterior da lista e confira se "Desktop" continua marcado em **Família de dispositivos**. Clique em **Salvar**.
4. **Listagens da Store**: abra cada um dos seis idiomas (Português (Brasil), Inglês (Estados Unidos), Espanhol
   (Espanha), Português (Portugal), Francês (França) e Alemão (Alemanha)). Em cada um:
   - substitua "O que há de novo nesta versão" pelo texto do `LISTAGEM_*.md` do idioma;
   - confira a descrição, o site e o contato de suporte com o arquivo do idioma (os endereços usam o `?lang=` do
     idioma);
   - se as capturas mudaram, apague as antigas e envie as novas, na ordem dos números, com as legendas do arquivo;
   - clique em **Salvar**.
5. **Preço e disponibilidade**, **Propriedades** e **Classificação etária** só precisam ser revistos se algo mudou.
6. **Opções de envio**: confira se as notas para a certificação (passo 7) continuam valendo.
7. Clique em **Enviar para a Store**. Acompanhe em **Status da certificação**. Se a Microsoft recusar, o relatório
   mostra o motivo; corrija, gere o pacote de novo (sem aumentar a versão, se ela ainda não foi publicada) e envie.
8. Depois da publicação, a página da loja e os botões "Microsoft Store" do site mostram a versão nova sozinhos. Quem já
   instalou recebe a atualização pela própria Store.
