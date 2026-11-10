# Instalador e assinatura de código

Instalador do **Meu Editor de Vídeo** para Windows 11 x64 (CA022, RNF017, ENT004), feito com o WiX Toolset 6.0.2.

## O que é gerado

`pwsh scripts\build-installer.ps1` cria, em `artifacts\installer\`:

| Arquivo | Para quê |
|---|---|
| `MeuEditorDeVideo-<versão>-Setup.exe` | **Instalador recomendado.** Instala o Visual C++ Redistributable x64 (se faltar) e, em seguida, o aplicativo para todos os usuários (pede elevação). |
| `MeuEditorDeVideo-<versão>-x64.msi` | Instalador do aplicativo. Serve para computadores que já têm o Visual C++ 2015-2026 (14.40 ou mais novo) e para distribuição corporativa (GPO, Intune). Permite escolher "somente para mim" (sem administrador) ou "para todos os usuários". |
| `SHA256SUMS.txt` | Hashes SHA-256 dos dois arquivos, para conferência. |

O script:

1. publica o aplicativo (`dotnet publish`, Release, win-x64, self-contained: o .NET vai junto) em `artifacts\publish`;
2. converte `LICENSE` (GPL-3.0) em RTF para a tela de licença;
3. compila `installer\Msi\MeuEditorDeVideo.Installer.wixproj` e `installer\Bundle\MeuEditorDeVideo.Setup.wixproj`;
4. assina, se houver certificado (ver abaixo).

Pré-requisitos: .NET SDK 10 e `pwsh scripts\fetch-deps.ps1` já executado (FFmpeg, MODNet e `VC_redist.x64.exe`, todos
verificados por SHA-256). O WiX vem do NuGet; nada precisa ser instalado. A versão vem de `Directory.Build.props`.

## Conteúdo do MSI

- Aplicativo publicado (≈ 540 MB instalados), sem símbolos de depuração (`*.pdb`) e sem bibliotecas de outras arquiteturas
  (`runtimes\win-x86`, `runtimes\win-arm64`).
- FFmpeg (`ffmpeg\`), modelo MODNet (`ai-models\modnet-retrato\`), catálogo de modelos opcionais (`catalogo-ia\`),
  modelos de exportação (`templates\`), manual do usuário (`ajuda\`), `LICENSE.txt` e `THIRD-PARTY-NOTICES.txt`.
- Atalho no menu Iniciar e, opcionalmente (recurso marcado por padrão), na área de trabalho.
- Associação dos arquivos `.med` (ProgId `MeuEditorDeVideo.Projeto`, ícone do aplicativo): um duplo clique abre o projeto.
- Entrada em **Configurações › Aplicativos** com ícone, versão e editor; atualização por *major upgrade* (instalar uma
  versão nova remove a anterior; instalar uma versão mais antiga por cima é bloqueado com mensagem em pt-BR).

Pastas de instalação:

| Modo | Pasta |
|---|---|
| Para todos os usuários | `C:\Program Files\Meu Editor de Vídeo` |
| Somente para mim | `%LOCALAPPDATA%\Apps\Meu Editor de Vídeo` |

## Desinstalação

Pelo Windows (**Configurações › Aplicativos**) ou `msiexec /x MeuEditorDeVideo-<versão>-x64.msi`. Remove os arquivos do
programa, os atalhos, a associação `.med` e as chaves de registro do instalador.

**Os dados do usuário são preservados**: projetos `.med`, configurações (`%APPDATA%\MeuEditorDeVideo`), modelos de IA
baixados, salvamentos automáticos e cache (`%LOCALAPPDATA%\MeuEditorDeVideo`). Assim, reinstalar ou atualizar não perde
nada. Para limpar o cache antes de desinstalar: **Ferramentas › Configurações… › Cache › Limpar cache**. Para remover
tudo, apague também as duas pastas acima.

## Instalação silenciosa (administradores)

```powershell
# Para todos os usuários (administrador), sem interface:
MeuEditorDeVideo-1.0.0-Setup.exe /quiet /norestart
msiexec /i MeuEditorDeVideo-1.0.0-x64.msi /qn ALLUSERS=1

# Somente para o usuário atual, sem administrador:
msiexec /i MeuEditorDeVideo-1.0.0-x64.msi /qn ALLUSERS=2 MSIINSTALLPERUSER=1

# Sem o atalho da área de trabalho:
msiexec /i MeuEditorDeVideo-1.0.0-x64.msi /qn ALLUSERS=1 ADDLOCAL=Aplicativo

# Log detalhado:
msiexec /i MeuEditorDeVideo-1.0.0-x64.msi /l*v instalacao.log
```

## Assinatura de código

Hoje **não há certificado** de assinatura de código (decisão do produto). Sem assinatura, o Windows SmartScreen mostra
"O Windows protegeu o computador / Editor desconhecido" no primeiro download; o usuário segue em **Mais informações ›
Executar assim mesmo**. A instalação e o funcionamento não mudam.

Quando houver um certificado (OV ou EV, emitido para "Diego Mendes Rodrigues"), a assinatura já está pronta no script:

```powershell
# Certificado instalado no repositório do Windows (ou num token USB/HSM, no caso de EV):
pwsh scripts\build-installer.ps1 -CertificateThumbprint <impressão digital SHA-1>

# Certificado em arquivo .pfx:
pwsh scripts\build-installer.ps1 -CertificatePath C:\certificados\codigo.pfx -CertificatePassword <senha>

# Outro servidor de carimbo de tempo ou signtool fora do Windows SDK:
pwsh scripts\build-installer.ps1 -CertificateThumbprint <...> -TimestampUrl http://timestamp.sectigo.com -SignToolPath <caminho>
```

O script assina, com SHA-256 e carimbo de tempo RFC 3161:

1. `MeuEditorDeVideo.exe`, antes de empacotar (o executável instalado sai assinado);
2. o MSI;
3. o motor do `Setup.exe` (`wix burn detach` → assinar → `wix burn reattach`) e, por fim, o próprio `Setup.exe`.

Requer o `signtool.exe` do Windows SDK. Conferir depois com `signtool verify /pa /v <arquivo>`. Nunca versionar o `.pfx` nem a
senha; em CI, guardar o certificado em um segredo do repositório ou usar um serviço de assinatura em nuvem.

## Testes do instalador (F12)

Verificado com o MSI 1.0.0 em instalação silenciosa "somente para mim":

| Verificação | Resultado |
|---|---|
| Instalação sem administrador (`msiexec /qn ALLUSERS=2 MSIINSTALLPERUSER=1`) | código 0; 358 arquivos, 541 MB; sem `.pdb`, `win-x86` ou `win-arm64` |
| Atalhos no menu Iniciar e na área de trabalho | criados |
| Associação `.med` | `"…\MeuEditorDeVideo.exe" "%1"`; abrir um `.med` pelo Windows iniciou o aplicativo instalado e carregou o projeto |
| Registro do produto | "Meu Editor de Vídeo" 1.0.0 no Windows Installer |
| Desinstalação (`msiexec /x … /qn`) | código 0; pasta, atalhos, ProgId, extensão e chave `HKCU\Software\Meu Editor de Vídeo` removidos; configurações do usuário mantidas |
