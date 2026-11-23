[English](INSTALL.md) | **Português (Brasil)**

# Instalação

Este guia tem duas partes:

1. [Instalar o aplicativo (usuário final)](#instalar-o-aplicativo-usuário-final): [Windows](#windows),
   [Linux](#linux-debian-e-ubuntu) e [macOS](#macos)
2. [Preparar o ambiente de desenvolvimento](#preparar-o-ambiente-de-desenvolvimento) no Windows, no Linux e no macOS

---

## Instalar o aplicativo (usuário final)

### Requisitos do computador

| | Mínimo | Recomendado |
|---|---|---|
| Sistema | Windows 11 64 bits (x64); Debian 12+ ou Ubuntu 22.04+ (x64); macOS 12+ (Apple Silicon ou Intel) | O mesmo |
| Processador | Intel Core i5 ou AMD Ryzen 5 (ou Apple Silicon M1) | Intel Core i7 ou AMD Ryzen 7 |
| Memória | 16 GB | 32 GB |
| Placa de vídeo | Compatível com Direct3D no Windows; qualquer uma no Linux e no macOS | NVIDIA RTX ou AMD Radeon com 8 GB |
| Armazenamento | SSD | SSD NVMe de 1 TB |

O programa ocupa cerca de 540 MB instalado no Windows. O .NET já vai junto: não é preciso instalá-lo à parte.

No Linux, os recursos de IA rodam no processador. Nos Macs com Apple Silicon usam o Core ML. **Macs com Intel não têm
ONNX Runtime**: os recursos de IA que dependem dele (como remover fundo e o assistente virtual) ficam desativados ali,
com aviso; as legendas com Whisper continuam funcionando.

### Windows

#### Qual instalador baixar

Os instaladores são publicados na página **Releases** do repositório no GitHub, quando há uma versão disponível.

| Arquivo | Quando usar |
|---|---|
| `MeuEditorDeVideo-<versão>-Setup.exe` | **Recomendado.** Instala o Visual C++ Redistributable x64 se faltar e, em seguida, o programa para todos os usuários, em `C:\Program Files\Meu Editor de Vídeo` (pede permissão de administrador). |
| `MeuEditorDeVideo-<versão>-x64.msi` | Para computadores que já têm o Visual C++ 2015-2026 (14.40 ou mais novo) e para distribuição em empresas (GPO, Intune). Permite escolher **somente para mim** (sem administrador, em `%LOCALAPPDATA%\Apps\Meu Editor de Vídeo`) ou **para todos os usuários**. |
| `SHA256SUMS.txt` | Somas SHA-256 para conferir os arquivos baixados. |

Para conferir um arquivo baixado, no PowerShell:

```powershell
Get-FileHash .\MeuEditorDeVideo-1.4.0-Setup.exe -Algorithm SHA256
```

O resultado deve ser igual à linha correspondente em `SHA256SUMS.txt`.

#### Instalar

1. Execute o `Setup.exe` (ou o `.msi`) e siga as telas.
2. Os instaladores não têm assinatura de código. Por isso, no primeiro download o Windows SmartScreen pode mostrar
   "O Windows protegeu o computador". Clique em **Mais informações › Executar assim mesmo**. A instalação e o
   funcionamento não mudam.
3. Abra o programa pelo menu Iniciar ou pelo atalho da área de trabalho. Os arquivos `.med` passam a abrir no editor.
4. Na primeira execução, a janela de **boas-vindas** pede seu nome, o idioma, o tema e se o programa deve verificar
   atualizações.

### Linux (Debian e Ubuntu)

Suportados: Debian 12 ou mais novo e Ubuntu 22.04 ou mais novo, 64 bits (x64). Os pacotes são arquivos `.deb`,
publicados na página **Releases** do repositório junto com o `SHA256SUMS.txt`.

```bash
sha256sum -c SHA256SUMS.txt --ignore-missing
sudo apt install ./meu-editor-de-video_<versão>_amd64.deb
```

O `apt` instala junto as bibliotecas necessárias (é preciso o `./` antes do nome do arquivo). O programa vai para
`/opt/meu-editor-de-video`, com o comando `meu-editor-de-video`, a entrada no menu de aplicativos, os ícones e o tipo
de arquivo `.med`. Para a narração (**Texto para fala**), instale também o `espeak-ng` (o pacote o recomenda). Para
instalar sem os pacotes recomendados: `sudo apt install --no-install-recommends ./…deb`.

- **A gravação de tela exige uma sessão Xorg (X11).** No Wayland (o padrão com GNOME no Ubuntu e no Debian) o sistema
  não deixa outros programas capturarem a tela: a gravação fica indisponível, com aviso, e o **F10** só funciona com o
  programa em primeiro plano. Para gravar, escolha uma sessão **Xorg** na tela de entrada. A câmera e o microfone
  funcionam nas duas.
- A IA não usa a placa de vídeo (roda no processador), e a codificação de vídeo pela placa é NVENC ou QSV; o VA-API
  ainda não está ligado.
- Abra pelo menu ou pelo comando `meu-editor-de-video`; os arquivos `.med` abrem no editor.

### macOS

Suportados: macOS 12 ou mais novo, em Apple Silicon (`arm64`) e Intel (`x64`); cada arquitetura tem o seu `.dmg`
(`MeuEditorDeVideo-<versão>-arm64.dmg` e `MeuEditorDeVideo-<versão>-x64.dmg`).

1. Abra o `.dmg` e arraste o **Meu Editor de Vídeo** para **Aplicativos**.
2. **Primeira abertura.** Se o pacote não foi assinado e notarizado, o Gatekeeper do macOS o bloqueia. Abra
   **Ajustes do Sistema › Privacidade e Segurança** e clique em **Abrir mesmo assim** (no macOS 14 e anteriores: clique
   com o botão direito no aplicativo › **Abrir**).
3. Na primeira vez em que você gravar a tela, usar a câmera ou o microfone, ou apertar **F10** com o programa em
   segundo plano, o macOS pede a permissão correspondente em **Ajustes do Sistema › Privacidade e Segurança**
   (Gravação de Tela, Câmera, Microfone e Acessibilidade). Se a gravação sair preta ou o **F10** não responder,
   confira essas permissões.
4. Nos teclados de Mac, use **Cmd (⌘)** e **Option (⌥)** onde o manual e a lista de atalhos dizem Ctrl e Alt; F1, F10 e
   F11 podem exigir a tecla **fn**. Os menus são a barra de menus nativa.
5. **Macs com Intel** não têm ONNX Runtime (veja os requisitos acima).

### Modelos de IA opcionais

A remoção de fundo (MODNet) já vem instalada. Os demais modelos (legendas com Whisper, isolar voz, detectar cenas,
retoque de rosto, assistente virtual com Phi-4-mini e outros) são instalados pelo próprio programa, em
**Ferramentas › Modelos de IA… › Baixar modelos…**, com o botão **Instalar**. Cada modelo é baixado uma única vez da origem
oficial e conferido por SHA-256. Detalhes no [manual do usuário](documentation/MANUAL_DO_USUARIO.md).

### Instalação silenciosa (Windows, administradores)

```powershell
# Setup.exe: Visual C++ (se faltar) + programa para todos os usuários
MeuEditorDeVideo-1.4.0-Setup.exe /quiet /norestart

# MSI para todos os usuários
msiexec /i MeuEditorDeVideo-1.4.0-x64.msi /qn ALLUSERS=1

# MSI somente para o usuário atual (sem administrador)
msiexec /i MeuEditorDeVideo-1.4.0-x64.msi /qn ALLUSERS=2 MSIINSTALLPERUSER=1

# Sem o atalho na área de trabalho
msiexec /i MeuEditorDeVideo-1.4.0-x64.msi /qn ALLUSERS=1 ADDLOCAL=Aplicativo

# Com registro detalhado da instalação
msiexec /i MeuEditorDeVideo-1.4.0-x64.msi /l*v instalacao.log
```

### Microsoft Store

O programa também pode ser distribuído pela Microsoft Store (pacote MSIX). Nesse caso a Microsoft assina o pacote e
o aviso do SmartScreen não aparece. Ver [`documentation/MICROSOFT_STORE.md`](documentation/MICROSOFT_STORE.md).

### Atualizar

Use **Ajuda › Verificar atualizações**. Se a opção estiver ligada, o programa também verifica sozinho a cada 30 dias
(enviando apenas o idioma da interface e o sistema operacional). Para atualizar, instale a nova versão por cima da atual (instalador do Windows,
`sudo apt install ./…deb` no Linux, ou trocando o aplicativo em **Aplicativos** no macOS): os projetos, as
configurações e os modelos de IA são mantidos.

### Desinstalar

**Linux:** `sudo apt remove meu-editor-de-video` (o pacote não guarda dados seus). **macOS:** arraste o aplicativo de
**Aplicativos** para a Lixeira. Nos dois casos os dados do usuário ficam; para removê-los, apague as pastas do usuário
(no Linux, `~/.config/MeuEditorDeVideo` e `~/.local/share/MeuEditorDeVideo`; no macOS,
`~/Library/Application Support/MeuEditorDeVideo`; **Ajuda › Abrir pasta de logs** mostra onde o programa guarda os dados
no seu sistema).

**Windows:** pelo Windows, em **Configurações › Aplicativos › Meu Editor de Vídeo › Desinstalar**, ou com:

```powershell
msiexec /x MeuEditorDeVideo-1.4.0-x64.msi
```

A desinstalação remove os arquivos do programa, os atalhos, a associação `.med` e as preferências do registro
(`HKCU\Software\Meu Editor de Vídeo`). **Os dados do usuário são preservados**:

- `%APPDATA%\MeuEditorDeVideo`: configurações e projetos recentes;
- `%LOCALAPPDATA%\MeuEditorDeVideo`: modelos de IA baixados, salvamentos automáticos, cache e fila de renderização;
- seus projetos `.med` e mídias, onde você os salvou.

Para remover tudo, apague também essas duas pastas. Para limpar só o cache antes de desinstalar, use
**Ferramentas › Configurações… › Cache › Limpar cache**.

Mais detalhes: [`documentation/INSTALADOR.md`](documentation/INSTALADOR.md).

---

## Preparar o ambiente de desenvolvimento

### Pré-requisitos

A mesma base de código compila no **Windows 11 x64**, no **Linux x64** (Debian 12+, Ubuntu 22.04+) e no **macOS** (arm64
e x64). Linux e macOS são validados só pelo CI; os passos abaixo marcam o que muda em cada sistema.

| Ferramenta | Uso |
|---|---|
| Windows 11 x64, Debian 12+/Ubuntu 22.04+ x64 ou macOS 12+ | Sistema em que compilar |
| [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.4xx) | Compilar e testar |
| [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) (`pwsh`) | Scripts de dependências, instaladores e verificações |
| [Git](https://git-scm.com/downloads) | Clonar o repositório |
| Xcode Command Line Tools e `brew install cmake pkg-config nasm` (só no macOS) | O `fetch-deps` compila o FFmpeg a partir do código-fonte no macOS (`nasm` só no Intel) |
| `dpkg-deb` (Linux, opcional) | `build-deb.ps1`; sem ele o script monta o `.deb` sozinho |
| Visual Studio 2026 ou VS Code com C# Dev Kit (opcional) | Editar e depurar |
| Python 3 (opcional) | `scripts/extract-strings.py`, ao criar ou mudar textos visíveis |
| Windows SDK (opcional) | `makeappx`, `makepri` e `signtool`, para gerar o pacote MSIX |

O WiX Toolset (instaladores MSI e Setup.exe) vem do NuGet: não precisa ser instalado.

### 1. Clonar o repositório

```powershell
git clone https://github.com/DiegoMendesRodrigues/Meu-Editor-de-Video.git
cd Meu-Editor-de-Video
```

### 2. Baixar as dependências nativas

```powershell
pwsh scripts/fetch-deps.ps1
```

Preenche `deps/` com as bibliotecas nativas do sistema em que roda, conferindo cada arquivo pelo SHA-256 registrado em
`scripts/deps.lock.json`:

- **Windows:** o FFmpeg, o modelo MODNet e o Visual C++ Redistributable.
- **Linux:** o build compartilhado do FFmpeg (BtbN) e o modelo MODNet.
- **macOS:** compila o FFmpeg a partir do código-fonte com `scripts/macos/build-ffmpeg.sh` (demora; precisa das
  ferramentas da tabela acima) e baixa o modelo MODNet.

Opções: `-Force` baixa de novo; `-Strict` falha se alguma dependência não tiver SHA-256 registrado. No Linux e no macOS
a estrutura é `deps/ffmpeg/{bin,lib}`.

### 3. Compilar

```powershell
dotnet build MeuEditorDeVideo.sln
```

O projeto usa `TreatWarningsAsErrors`: a compilação precisa terminar com **0 avisos**. As versões dos pacotes ficam
em `Directory.Packages.props`, e a versão do programa é a única `<Version>` em `Directory.Build.props`.

### 4. Testar

```powershell
# Tudo
dotnet test MeuEditorDeVideo.sln

# Uma classe
dotnet test tests/MeuEditorDeVideo.UnitTests --filter "FullyQualifiedName~RecentProjectRemovalTests"

# Regras de arquitetura (camadas e privacidade)
dotnet test MeuEditorDeVideo.sln --filter "FullyQualifiedName~Architecture"

# Testes de desempenho
dotnet test tests/MeuEditorDeVideo.MediaTests --filter "Categoria=Desempenho"
```

- `MediaTests` e `IntegrationTests` precisam de `deps\ffmpeg` (passo 2). Eles geram mídias de teste com o
  `ffmpeg.exe` numa pasta temporária com acentos no caminho.
- Os testes de IA são ignorados quando os arquivos dos modelos não existem. Os modelos são procurados em
  `%MED_MODELS_DIR%`, em `deps\ai-models` e nos modelos instalados pelo programa.
- Os testes `PrivacyTests` falham se qualquer assembly além de `ModelDownloads` usar `System.Net.*`.

### 5. Executar

```powershell
dotnet run --project src/MeuEditorDeVideo.Desktop
```

O executável fica em `src/MeuEditorDeVideo.Desktop/bin/Debug/net10.0/MeuEditorDeVideo.exe` (`MeuEditorDeVideo`, sem
extensão, no Linux e no macOS).

### Variáveis de ambiente

| Variável | Efeito |
|---|---|
| `MED_FFMPEG_DIR` | Pasta das bibliotecas do FFmpeg, no lugar de `deps/ffmpeg` |
| `MED_MODELS_DIR` | Pasta dos modelos de IA usados pelos testes |
| `MED_AUDIO_BACKEND` | `sdl` usa o SDL3 para o áudio também no Windows (o padrão ali é o WASAPI); Linux e macOS sempre usam o SDL3 |

### Gerar os instaladores e outras tarefas

```powershell
# MSI (installer/Msi) + Setup.exe (installer/Bundle) em artifacts/installer
pwsh scripts/build-installer.ps1

# Pacote MSIX para a Microsoft Store em artifacts/store (-TestSign para testar a instalação localmente)
pwsh scripts/build-msix.ps1

# Regerar THIRD-PARTY-NOTICES.txt depois de mudar qualquer dependência
pwsh scripts/generate-notices.ps1

# Conferir, com o programa em execução, que ele não abre conexões de rede
pwsh scripts/offline-check.ps1

# Extrair os textos visíveis para resources/i18n/origem.json (depois, traduzir nos cinco .tsv)
python scripts/extract-strings.py
```

O `build-installer.ps1` aceita assinatura de código com `-CertificateThumbprint` ou `-CertificatePath` e
`-CertificatePassword` (além de `-TimestampUrl` e `-SignToolPath`). Ver
[`documentation/INSTALADOR.md`](documentation/INSTALADOR.md).

### Pacotes para Linux e macOS

O CI (`.github/workflows/build.yml`) roda em `windows-latest`, `ubuntu-22.04`, `macos-15` (arm64) e `macos-15-intel`
(x64): compila, executa a suíte inteira e, nos pushes para `main`, gera os pacotes e os instala num ambiente limpo
(instaladores no Windows e no macOS, `.deb` em contêineres Debian 12, Ubuntu 22.04 e 24.04).

Os dois scripts publicam o programa autocontido para o sistema de destino (`dotnet publish -r <rid> --self-contained`) e
empacotam o resultado. Rode antes `pwsh scripts/fetch-deps.ps1` no mesmo sistema (ele preenche `deps/` com as
bibliotecas nativas desse sistema).

```powershell
# Pacote Debian/Ubuntu (.deb) em artifacts/linux, com SHA256SUMS.txt
pwsh scripts/build-deb.ps1 -Maintainer "Nome <email>"   # ou defina MED_DEB_MAINTAINER

# Aplicativo do macOS (Meu Editor de Vídeo.app) e imagem de disco (.dmg) em artifacts/macos, uma execução por arquitetura
pwsh scripts/build-macos.ps1 -Rid osx-arm64      # Apple silicon
pwsh scripts/build-macos.ps1 -Rid osx-x64        # Intel
```

**`.deb` do Linux** (Debian 12+, Ubuntu 22.04+, x64). O pacote instala a pasta inteira da publicação em
`/opt/meu-editor-de-video/`, o comando `meu-editor-de-video` em `/usr/bin`, a entrada no menu, o tipo de arquivo `.med`
e os ícones. Usa `dpkg-deb --root-owner-group` quando existir (qualquer Debian/Ubuntu, ou `apt install dpkg`); senão o
script grava o `.deb` por conta própria, o que também funciona no Windows e no macOS para testar a estrutura. A lista
de dependências fica no começo de `scripts/build-deb.ps1` (veja `documentation/DEPENDENCIAS.md`). Instale com
`sudo apt install ./meu-editor-de-video_<versão>_amd64.deb`.

**`.app` / `.dmg` do macOS**. O pacote coloca a pasta inteira da publicação em `Contents/MacOS`, com o `Info.plist`, o
ícone (`.icns`) e os textos das permissões de câmera e microfone nos seis idiomas. O tipo `.med` é declarado no
`Info.plist`. A gravação de tela não tem chave no `Info.plist`: o macOS pergunta na primeira gravação (Ajustes do
Sistema › Privacidade e Segurança › Gravação de Tela). O `.dmg` precisa do macOS (`hdiutil`); em outros sistemas o
script só monta a pasta `.app` e um `.zip` para conferir a estrutura, que não é um aplicativo utilizável (sem
`codesign` nem `plutil`).

A assinatura e a notarização são opcionais e leem a configuração de variáveis de ambiente, nunca do repositório:

| Variável | Usada por | Significado |
|---|---|---|
| `MED_MACOS_SIGN_IDENTITY` | `-Sign` | Nome do certificado, por exemplo `Developer ID Application: Nome (TEAMID)` (precisa estar no chaveiro) |
| `MED_MACOS_NOTARY_PROFILE` | `-Notarize` | Perfil criado com `xcrun notarytool store-credentials` |
| `MED_MACOS_NOTARY_KEYCHAIN` | `-Notarize` | Opcional: arquivo do chaveiro que guarda esse perfil, quando não é o padrão |

```bash
export MED_MACOS_SIGN_IDENTITY="Developer ID Application: Nome (TEAMID)"
export MED_MACOS_NOTARY_PROFILE="med-notary"
pwsh scripts/build-macos.ps1 -Rid osx-arm64 -Sign -Notarize
```

`-Sign` assina cada biblioteca nativa e a ferramenta `ffmpeg`, depois o aplicativo, com o runtime reforçado e
`packaging/macos/MeuEditorDeVideo.entitlements`. `-Notarize` envia o aplicativo e o `.dmg` à Apple e grampeia os
tíquetes. Sem eles, o aplicativo recebe só uma assinatura ad hoc e o Gatekeeper do macOS bloqueia a primeira abertura: libere em
Ajustes do Sistema › Privacidade e Segurança › Abrir Mesmo Assim (no macOS 14 e anteriores, botão direito › Abrir). O CI
(`.github/workflows/build.yml`) compila os quatro destinos e, nos envios para `main`, gera estes pacotes; assina e
notariza quando existem os segredos `MACOS_SIGN_IDENTITY`, `MACOS_CERT_P12_BASE64`, `MACOS_CERT_PASSWORD`, `APPLE_ID`,
`APPLE_TEAM_ID` e `APPLE_APP_PASSWORD` no repositório.

### Cuidados ao testar o programa real

- Enquanto o programa está aberto no Windows, as DLLs em `bin\` ficam bloqueadas e `dotnet build`/`dotnet test` falham
  com MSB3027. Feche o programa antes, ou compile numa pasta separada com `-o` só para conferir a compilação.
- O programa usa os seus dados reais: `%APPDATA%\MeuEditorDeVideo\settings.json` (projetos recentes),
  `%LOCALAPPDATA%\MeuEditorDeVideo\` (salvamento automático, cache, modelos de IA e fila de renderização) e as
  preferências em `HKCU\Software\Meu Editor de Vídeo`. Faça cópia de segurança antes de testar. Para testar a primeira
  execução (janela de boas-vindas), esvazie essa chave do registro. No Linux e no macOS esses dados ficam nas pastas do
  usuário (`~/.config/MeuEditorDeVideo` e `~/.local/share/MeuEditorDeVideo` no Linux;
  `~/Library/Application Support/MeuEditorDeVideo` no macOS) e as preferências são o arquivo `preferences.json` da pasta
  de configurações: apague-o para testar a primeira execução.
- `scripts/ci/testar-instalador.ps1` e `scripts/ci/testar-deb.sh` instalam o programa e **apagam os dados do usuário**:
  servem só para o CI ou para um contêiner descartável.
- `pwsh scripts/offline-check.ps1` também funciona no Linux (`ss`) e no macOS (`lsof`).

### Leitura recomendada

- Arquitetura e receitas para estender (efeitos, transições, modelos de exportação, modelos de IA, atalhos e
  migrações do `.med`): [`documentation/DOCUMENTACAO_TECNICA.md`](documentation/DOCUMENTACAO_TECNICA.md)
- Dependências e licenças: [`documentation/DEPENDENCIAS.md`](documentation/DEPENDENCIAS.md)
- Modelos de IA: [`documentation/MODELOS_IA.md`](documentation/MODELOS_IA.md)
