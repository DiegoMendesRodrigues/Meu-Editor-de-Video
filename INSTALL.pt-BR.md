[English](INSTALL.md) | **Português (Brasil)**

# Instalação

Este guia tem duas partes:

1. [Instalar o aplicativo (usuário final)](#instalar-o-aplicativo-usuário-final)
2. [Preparar o ambiente de desenvolvimento](#preparar-o-ambiente-de-desenvolvimento)

---

## Instalar o aplicativo (usuário final)

### Requisitos do computador

| | Mínimo | Recomendado |
|---|---|---|
| Sistema | Windows 11 64 bits (x64) | Windows 11 64 bits (x64) |
| Processador | Intel Core i5 ou AMD Ryzen 5 | Intel Core i7 ou AMD Ryzen 7 |
| Memória | 16 GB | 32 GB |
| Placa de vídeo | Compatível com Direct3D | NVIDIA RTX ou AMD Radeon com 8 GB |
| Armazenamento | SSD | SSD NVMe de 1 TB |

O programa ocupa cerca de 540 MB instalado. O .NET já vai junto: não é preciso instalá-lo à parte.

### Qual instalador baixar

Os instaladores são publicados na página **Releases** do repositório no GitHub, quando há uma versão disponível.

| Arquivo | Quando usar |
|---|---|
| `MeuEditorDeVideo-<versão>-Setup.exe` | **Recomendado.** Instala o Visual C++ Redistributable x64 se faltar e, em seguida, o programa para todos os usuários, em `C:\Program Files\Meu Editor de Vídeo` (pede permissão de administrador). |
| `MeuEditorDeVideo-<versão>-x64.msi` | Para computadores que já têm o Visual C++ 2015-2026 (14.40 ou mais novo) e para distribuição em empresas (GPO, Intune). Permite escolher **somente para mim** (sem administrador, em `%LOCALAPPDATA%\Apps\Meu Editor de Vídeo`) ou **para todos os usuários**. |
| `SHA256SUMS.txt` | Somas SHA-256 para conferir os arquivos baixados. |

Para conferir um arquivo baixado, no PowerShell:

```powershell
Get-FileHash .\MeuEditorDeVideo-1.1.0-Setup.exe -Algorithm SHA256
```

O resultado deve ser igual à linha correspondente em `SHA256SUMS.txt`.

### Instalar

1. Execute o `Setup.exe` (ou o `.msi`) e siga as telas.
2. Os instaladores não têm assinatura de código. Por isso, no primeiro download o Windows SmartScreen pode mostrar
   "O Windows protegeu o computador". Clique em **Mais informações › Executar assim mesmo**. A instalação e o
   funcionamento não mudam.
3. Abra o programa pelo menu Iniciar ou pelo atalho da área de trabalho. Os arquivos `.med` passam a abrir no editor.
4. Na primeira execução, a janela de **boas-vindas** pede seu nome, o idioma, o tema e se o programa deve verificar
   atualizações.

### Modelos de IA opcionais

A remoção de fundo (MODNet) já vem instalada. Os demais modelos (legendas com Whisper, isolar voz, detectar cenas,
retoque de rosto, assistente virtual com Phi-4-mini e outros) são instalados pelo próprio programa, em
**Ferramentas › Modelos de IA… › Baixar modelos…**, com o botão **Instalar**. Cada modelo é baixado uma única vez da origem
oficial e conferido por SHA-256. Detalhes no [manual do usuário](documentation/MANUAL_DO_USUARIO.md).

### Instalação silenciosa (administradores)

```powershell
# Setup.exe: Visual C++ (se faltar) + programa para todos os usuários
MeuEditorDeVideo-1.1.0-Setup.exe /quiet /norestart

# MSI para todos os usuários
msiexec /i MeuEditorDeVideo-1.1.0-x64.msi /qn ALLUSERS=1

# MSI somente para o usuário atual (sem administrador)
msiexec /i MeuEditorDeVideo-1.1.0-x64.msi /qn ALLUSERS=2 MSIINSTALLPERUSER=1

# Sem o atalho na área de trabalho
msiexec /i MeuEditorDeVideo-1.1.0-x64.msi /qn ALLUSERS=1 ADDLOCAL=Aplicativo

# Com registro detalhado da instalação
msiexec /i MeuEditorDeVideo-1.1.0-x64.msi /l*v instalacao.log
```

### Microsoft Store

O programa também pode ser distribuído pela Microsoft Store (pacote MSIX). Nesse caso a Microsoft assina o pacote e
o aviso do SmartScreen não aparece. Ver [`documentation/MICROSOFT_STORE.md`](documentation/MICROSOFT_STORE.md).

### Atualizar

Use **Ajuda › Verificar atualizações**. Se a opção estiver ligada, o programa também verifica sozinho a cada 30 dias
(enviando apenas o idioma da interface e o sistema operacional). Para atualizar, instale a nova versão por cima da atual: os projetos, as
configurações e os modelos de IA são mantidos.

### Desinstalar

Pelo Windows, em **Configurações › Aplicativos › Meu Editor de Vídeo › Desinstalar**, ou com:

```powershell
msiexec /x MeuEditorDeVideo-1.1.0-x64.msi
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

| Ferramenta | Uso |
|---|---|
| Windows 11 x64 | Único sistema suportado (Avalonia, DirectML, WASAPI) |
| [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.4xx) | Compilar e testar |
| [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) (`pwsh`) | Scripts de dependências, instaladores e verificações |
| [Git](https://git-scm.com/download/win) | Clonar o repositório |
| Visual Studio 2026 ou VS Code com C# Dev Kit (opcional) | Editar e depurar |
| Python 3 (opcional) | `scripts/extract-strings.py`, ao criar ou mudar textos visíveis |
| Windows SDK (opcional) | `makeappx`, `makepri` e `signtool`, para gerar o pacote MSIX |

O WiX Toolset (instaladores MSI e Setup.exe) vem do NuGet: não precisa ser instalado.

### 1. Clonar o repositório

```powershell
git clone https://github.com/DiegoMendesRodrigues/Meu-Editor-de-V-deo.git
cd Meu-Editor-de-V-deo
```

### 2. Baixar as dependências nativas

```powershell
pwsh scripts/fetch-deps.ps1
```

Baixa para `deps/` o FFmpeg, o modelo MODNet e o Visual C++ Redistributable, conferindo cada arquivo pelo SHA-256
registrado em `scripts/deps.lock.json`. Opções: `-Force` baixa de novo; `-Strict` falha se alguma dependência não
tiver SHA-256 registrado.

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

O executável fica em `src/MeuEditorDeVideo.Desktop/bin/Debug/net10.0/MeuEditorDeVideo.exe`.

### Variáveis de ambiente

| Variável | Efeito |
|---|---|
| `MED_FFMPEG_DIR` | Pasta das DLLs do FFmpeg, no lugar de `deps\ffmpeg` |
| `MED_MODELS_DIR` | Pasta dos modelos de IA usados pelos testes |

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
tíquetes. Sem eles, o `.dmg` fica sem assinatura e o Gatekeeper do macOS pede confirmação (botão direito › Abrir). O CI
(`.github/workflows/build.yml`) compila os quatro destinos e, nos envios para `main`, gera estes pacotes; assina e
notariza quando existem os segredos `MACOS_SIGN_IDENTITY`, `MACOS_CERT_P12_BASE64`, `MACOS_CERT_PASSWORD`, `APPLE_ID`,
`APPLE_TEAM_ID` e `APPLE_APP_PASSWORD` no repositório.

### Cuidados ao testar o programa real

- Enquanto o programa está aberto, as DLLs em `bin\` ficam bloqueadas e `dotnet build`/`dotnet test` falham com
  MSB3027. Feche o programa antes, ou compile numa pasta separada com `-o` só para conferir a compilação.
- O programa usa os seus dados reais: `%APPDATA%\MeuEditorDeVideo\settings.json` (projetos recentes),
  `%LOCALAPPDATA%\MeuEditorDeVideo\` (salvamento automático, cache, modelos de IA e fila de renderização) e as
  preferências em `HKCU\Software\Meu Editor de Vídeo`. Faça cópia de segurança antes de testar. Para testar a primeira
  execução (janela de boas-vindas), esvazie essa chave do registro.

### Leitura recomendada

- Arquitetura e receitas para estender (efeitos, transições, modelos de exportação, modelos de IA, atalhos e
  migrações do `.med`): [`documentation/DOCUMENTACAO_TECNICA.md`](documentation/DOCUMENTACAO_TECNICA.md)
- Dependências e licenças: [`documentation/DEPENDENCIAS.md`](documentation/DEPENDENCIAS.md)
- Modelos de IA: [`documentation/MODELOS_IA.md`](documentation/MODELOS_IA.md)
