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
| `installer/Store/LISTAGEM_PT-BR.md` | Textos da página da loja: descrição, recursos, palavras-chave, requisitos e legendas das capturas. |
| `installer/Store/capturas/` | Capturas de tela para a página da loja. |
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

- A versão vem de `Directory.Build.props` e vira `1.0.0.0`. A Store exige que a quarta parte seja `0`.
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
Add-AppxPackage -Path 'artifacts\store\MeuEditorDeVideo_1.0.0.0_x64.msix'
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

- Mercados: todos, ou só Brasil e países de língua portuguesa (o aplicativo existe apenas em pt-BR).
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

Adicione o idioma **Português (Brasil)** e copie os textos de `installer/Store/LISTAGEM_PT-BR.md`. Envie as capturas
de `installer/Store/capturas/`.

### Opções de envio: notas para a certificação

O pacote declara a capacidade restrita `runFullTrust`, e o Partner Center pede uma justificativa. Use:

```text
O Meu Editor de Vídeo é um aplicativo de área de trabalho Win32 (WPF/.NET 10) empacotado em MSIX. A capacidade runFullTrust é necessária para executar o aplicativo de desktop, que usa FFmpeg para decodificar e codificar vídeo, Direct3D/DirectML para aceleração por GPU e WASAPI para áudio.

Não é necessário login nem conta. Para testar: na tela inicial, clique em "Novo Projeto", importe um vídeo (Importar Mídia), adicione-o à linha do tempo e clique em "Exportar".

O aplicativo só usa a internet quando o usuário abre Ferramentas › Baixar modelos de IA e clica em Instalar: ele baixa modelos de IA (arquivos de dados ONNX/GGML, não executáveis) de endereços HTTPS fixos em huggingface.co e github.com, conferindo o SHA-256 de cada arquivo. Nenhum dado do usuário é enviado.
```

Clique em **Enviar para a Store**. A certificação costuma levar de algumas horas a três dias úteis.

## 8. Atualizações

1. Aumente `<Version>` em `Directory.Build.props`.
2. `pwsh scripts/build-msix.ps1`.
3. No Partner Center: **Atualizar** a submissão, substitua o pacote e preencha "O que há de novo nesta versão".
