# Meu Editor de Vídeo

Editor de vídeo desktop para **Windows 11 (64 bits)**, em **Português do Brasil**, com processamento 100% local
(inclusive IA). Licenciado sob **GPL-3.0-or-later**.

- Especificação: [`ESPECIFICACAO_TECNICA_E_FUNCIONAL.md`](ESPECIFICACAO_TECNICA_E_FUNCIONAL.md)
- Plano de implantação: [`documentation/PLANO_DE_IMPLANTACAO.md`](documentation/PLANO_DE_IMPLANTACAO.md)
- Manual do usuário: [`documentation/MANUAL_DO_USUARIO.md`](documentation/MANUAL_DO_USUARIO.md) (também em **Ajuda › Manual do usuário**, F1)
- Documentação técnica: [`documentation/DOCUMENTACAO_TECNICA.md`](documentation/DOCUMENTACAO_TECNICA.md)
- Instalador e assinatura de código: [`documentation/INSTALADOR.md`](documentation/INSTALADOR.md)
- Homologação (CA001–CA024): [`documentation/HOMOLOGACAO.md`](documentation/HOMOLOGACAO.md)
- Dependências e licenças: [`documentation/DEPENDENCIAS.md`](documentation/DEPENDENCIAS.md) e [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt)

## Requisitos de desenvolvimento

- Windows 11 x64
- .NET SDK 10.0.4xx
- PowerShell 7 (`pwsh`)

## Compilar e testar

```powershell
pwsh scripts/fetch-deps.ps1        # baixa e verifica FFmpeg, MODNet e Visual C++ Redistributable (SHA-256)
dotnet build MeuEditorDeVideo.sln
dotnet test MeuEditorDeVideo.sln
dotnet run --project src/MeuEditorDeVideo.App
```

## Instalador

```powershell
pwsh scripts/build-installer.ps1   # artifacts\installer\MeuEditorDeVideo-<versão>-Setup.exe e o .msi
```

Detalhes (conteúdo, instalação silenciosa, desinstalação e assinatura de código) em
[`documentation/INSTALADOR.md`](documentation/INSTALADOR.md).

## Estrutura

```
src/        App (WPF/MVVM), Application, Domain, Core, Infrastructure e motores
            (Project, Media, Timeline, Image, Audio, AI, Rendering)
tests/      UnitTests, IntegrationTests, MediaTests
resources/  modelos de exportação, transições, efeitos, LUTs, fontes
scripts/    dependências, avisos de terceiros, instalador e bateria offline
installer/  instalador WiX: Msi/ (MSI) e Bundle/ (Setup.exe com o Visual C++)
documentation/
```

## Privacidade

Todo o processamento (inclusive a IA) é local, sem telemetria. A única exceção de rede é a ferramenta
**Baixar modelos de IA**: quando o usuário clica em **Instalar**, ela baixa os modelos opcionais das URLs oficiais
fixadas no catálogo, só por HTTPS e com verificação SHA-256. Esse código fica isolado no assembly
`MeuEditorDeVideo.ModelDownloads`. Testes automatizados garantem que nenhum outro assembly usa rede
(`tests/MeuEditorDeVideo.UnitTests/Architecture/PrivacyTests.cs`). A bateria `pwsh scripts/offline-check.ps1`
confirma em execução que o aplicativo não abre conexões.
